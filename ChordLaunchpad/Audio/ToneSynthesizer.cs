using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;

namespace ChordLaunchpad.Audio;

/// <summary>
/// Web Audio API のオシレーター・エンベロープ合成を再現した和音サンプルプロバイダー
/// </summary>
public sealed class ToneSynthesizer : ISampleProvider
{
    private const int MaxVoices = 32;
    private readonly WaveFormat _waveFormat;
    private readonly Voice[] _voicePool = new Voice[MaxVoices];

    public WaveFormat WaveFormat => _waveFormat;

    public ToneSynthesizer(int sampleRate = 44100)
    {
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        for (int i = 0; i < MaxVoices; i++)
        {
            _voicePool[i] = new Voice();
        }
    }

    public void TriggerChord(IReadOnlyList<int> midiNotes, double durationMs, PlaybackTone tone)
    {
        if (midiNotes == null || midiNotes.Count == 0) return;

        double sampleRate = _waveFormat.SampleRate;
        double durationSec = Math.Max(0.1, durationMs / 1000.0);
        double chordGainScale = 1.0 / Math.Sqrt(Math.Max(1, midiNotes.Count));

        for (int i = 0; i < midiNotes.Count; i++)
        {
            int note = midiNotes[i];
            double freq = 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);

            // 1. 空きスロット (State == 0) を探してアトミックに確保 (0 -> 1: Initializing)
            Voice? targetVoice = null;
            for (int v = 0; v < MaxVoices; v++)
            {
                var candidate = _voicePool[v];
                if (Volatile.Read(ref candidate.State) == 0 && Interlocked.CompareExchange(ref candidate.State, 1, 0) == 0)
                {
                    targetVoice = candidate;
                    break;
                }
            }

            // 2. 空きスロットがない場合は、最も長く再生されている（最も減衰が進んでいる）Active スロットをスティーリング
            if (targetVoice == null)
            {
                long oldestSampleCount = -1;
                int oldestIndex = -1;
                for (int v = 0; v < MaxVoices; v++)
                {
                    var candidate = _voicePool[v];
                    if (Volatile.Read(ref candidate.State) == 2 && candidate.SampleCount > oldestSampleCount)
                    {
                        oldestSampleCount = candidate.SampleCount;
                        oldestIndex = v;
                    }
                }

                if (oldestIndex >= 0)
                {
                    var candidate = _voicePool[oldestIndex];
                    if (Interlocked.CompareExchange(ref candidate.State, 1, 2) == 2)
                    {
                        targetVoice = candidate;
                    }
                }
            }

            // スロットが確保できた場合は初期化して公開 (State -> 2: Active)
            if (targetVoice != null)
            {
                targetVoice.Init(freq, i, durationSec, tone, sampleRate, chordGainScale);
                Volatile.Write(ref targetVoice.State, 2);
            }
        }
    }

    public void StopAll()
    {
        for (int i = 0; i < MaxVoices; i++)
        {
            Volatile.Write(ref _voicePool[i].State, 0);
        }
    }

    public int Read(Span<float> buffer)
    {
        buffer.Clear();

        int frames = buffer.Length / 2;
        for (int f = 0; f < frames; f++)
        {
            float leftSample = 0f;
            float rightSample = 0f;

            for (int v = 0; v < MaxVoices; v++)
            {
                var voice = _voicePool[v];
                if (Volatile.Read(ref voice.State) != 2) continue;

                if (voice.IsFinished)
                {
                    Volatile.Write(ref voice.State, 0);
                    continue;
                }

                float s = voice.NextSample();
                leftSample += s;
                rightSample += s;
            }

            int outIndex = f * 2;
            buffer[outIndex] = SoftLimit(leftSample);
            buffer[outIndex + 1] = SoftLimit(rightSample);
        }

        return buffer.Length;
    }

    /// <summary>
    /// デジタルハードクリッピング（矩形波歪み・音割れ）を防ぎ、自然で温かいサチュレーションを行う高速な代数ソフトリミッター
    /// </summary>
    private static float SoftLimit(float sample)
    {
        const float threshold = 0.85f;
        const float headroom = 0.15f;
        if (sample > threshold)
        {
            float excess = sample - threshold;
            float x = excess / headroom;
            return threshold + headroom * (x / (1.0f + x));
        }
        if (sample < -threshold)
        {
            float excess = -sample - threshold;
            float x = excess / headroom;
            return -(threshold + headroom * (x / (1.0f + x)));
        }
        return sample;
    }

    private sealed class Voice
    {
        public int State; // 0: Free, 1: Initializing, 2: Active

        private double _frequency;
        private double _detuneFrequency;
        private int _noteIndex;
        private double _durationSec;
        private PlaybackTone _tone;
        private double _sampleRate;
        private double _chordGainScale;

        // Pad / Organ 用オシレーターフェーズおよび位相増分
        private double _phase1;
        private double _phase2;
        private double _dPhase1;
        private double _dPhase2;

        // Piano 用（マルチパーシャル + インハーモニシティ + ユニゾン + ハンマートランジェント）
        private double _d1, _d2, _d3, _d4, _d5, _d6, _d7;
        private double _dUnison1, _dUnison2;
        private double _dHammer;
        private double _p1, _p2, _p3, _p4, _p5, _p6, _p7;
        private double _pUnison1, _pUnison2;
        private double _pHammer;

        // Piano 用事前計算減衰乗数（等比数列によるホットパスの Math.Exp 完全排除）
        private double _decayMul1, _decayMul2, _decayMul3, _decayMul4, _decayMul5, _decayMul6, _decayMul7;
        private double _decayMulHammer;
        private double _e1 = 1.0, _e2 = 1.0, _e3 = 1.0, _e4 = 1.0, _e5 = 1.0, _e6 = 1.0, _e7 = 1.0;
        private double _eHammer = 1.0;

        private long _attackSamples;
        private long _releaseStartSamples;
        private long _releaseDurationSamples;

        private long _sampleCount;
        private long _totalDurationSamples;

        public bool IsFinished => _sampleCount >= _totalDurationSamples;
        public long SampleCount => _sampleCount;

        public Voice()
        {
            State = 0;
        }

        public void Init(double frequency, int noteIndex, double durationSec, PlaybackTone tone, double sampleRate, double chordGainScale)
        {
            _frequency = frequency;
            _noteIndex = noteIndex;
            _durationSec = durationSec;
            _tone = tone;
            _sampleRate = sampleRate;
            _chordGainScale = chordGainScale;

            _attackSamples = (long)(0.0025 * sampleRate);
            _releaseStartSamples = (long)(durationSec * sampleRate);
            _releaseDurationSamples = (long)(0.08 * sampleRate);

            _sampleCount = 0;
            _phase1 = 0;
            _phase2 = 0;

            _p1 = _p2 = _p3 = _p4 = _p5 = _p6 = _p7 = 0;
            _pUnison1 = _pUnison2 = 0;
            _pHammer = 0;
            _e1 = _e2 = _e3 = _e4 = _e5 = _e6 = _e7 = 1.0;
            _eHammer = 1.0;

            if (tone == PlaybackTone.Piano)
            {
                // ピアノ弦の曲げ剛性によるインハーモニシティ係数
                const double B = 0.00018;
                double f0 = frequency;

                double f1 = f0 * Math.Sqrt(1.0 + B * 1.0);
                double f2 = 2.0 * f0 * Math.Sqrt(1.0 + B * 4.0);
                double f3 = 3.0 * f0 * Math.Sqrt(1.0 + B * 9.0);
                double f4 = 4.0 * f0 * Math.Sqrt(1.0 + B * 16.0);
                double f5 = 5.0 * f0 * Math.Sqrt(1.0 + B * 25.0);
                double f6 = 6.0 * f0 * Math.Sqrt(1.0 + B * 36.0);
                double f7 = 7.0 * f0 * Math.Sqrt(1.0 + B * 49.0);

                // ユニゾン弦 (約 +1.2 セントずれた第2弦による温かいうねりとコーラス感)
                double unisonRatio = Math.Pow(2.0, 1.2 / 1200.0);
                double fUnison1 = f1 * unisonRatio;
                double fUnison2 = f2 * unisonRatio;

                // 打弦ハンマーの共振周波数 (180〜450Hz の木製フェルト打撃共振)
                double fHammer = Math.Clamp(f0 * 1.5, 180.0, 450.0);

                double nyquist = sampleRate * 0.45;
                _d1 = f1 / sampleRate;
                _d2 = (f2 < nyquist) ? (f2 / sampleRate) : 0.0;
                _d3 = (f3 < nyquist) ? (f3 / sampleRate) : 0.0;
                _d4 = (f4 < nyquist) ? (f4 / sampleRate) : 0.0;
                _d5 = (f5 < nyquist) ? (f5 / sampleRate) : 0.0;
                _d6 = (f6 < nyquist) ? (f6 / sampleRate) : 0.0;
                _d7 = (f7 < nyquist) ? (f7 / sampleRate) : 0.0;
                _dUnison1 = fUnison1 / sampleRate;
                _dUnison2 = (fUnison2 < nyquist) ? (fUnison2 / sampleRate) : 0.0;
                _dHammer = fHammer / sampleRate;

                // 周波数による減衰速度調整 (高音ほど早く減衰、低音ほど長く響く)
                double pitchFactor = Math.Clamp(frequency / 261.63, 0.5, 2.5);
                double baseDecay = 1.8 / Math.Max(0.5, durationSec);

                // 1サンプルあたりの減衰乗数を事前計算
                _decayMul1 = Math.Exp(-baseDecay * 0.8 / sampleRate);
                _decayMul2 = Math.Exp(-baseDecay * 1.3 * pitchFactor / sampleRate);
                _decayMul3 = Math.Exp(-baseDecay * 2.2 * pitchFactor / sampleRate);
                _decayMul4 = Math.Exp(-baseDecay * 3.6 * pitchFactor / sampleRate);
                _decayMul5 = Math.Exp(-baseDecay * 5.5 * pitchFactor / sampleRate);
                _decayMul6 = Math.Exp(-baseDecay * 8.0 * pitchFactor / sampleRate);
                _decayMul7 = Math.Exp(-baseDecay * 12.0 * pitchFactor / sampleRate);
                _decayMulHammer = Math.Exp(-1.0 / (0.002 * sampleRate));

                _detuneFrequency = 0;
                _dPhase1 = 0;
                _dPhase2 = 0;
                _totalDurationSamples = (long)((durationSec + 0.12) * sampleRate);
            }
            else if (tone == PlaybackTone.Pad)
            {
                double detuneCents = (noteIndex % 2 == 0) ? 6.0 : -6.0;
                _detuneFrequency = frequency * Math.Pow(2.0, detuneCents / 1200.0);
                _dPhase1 = frequency / sampleRate;
                _dPhase2 = _detuneFrequency / sampleRate;
                _totalDurationSamples = (long)((durationSec + 0.15) * sampleRate);
            }
            else // Organ
            {
                _detuneFrequency = frequency * 2.0; // 倍音
                _dPhase1 = frequency / sampleRate;
                _dPhase2 = _detuneFrequency / sampleRate;
                _totalDurationSamples = (long)((durationSec + 0.05) * sampleRate);
            }
        }

        public float NextSample()
        {
            if (IsFinished) return 0f;

            long currentSample = _sampleCount++;

            if (_tone == PlaybackTone.Piano)
            {
                return NextPianoSample(currentSample);
            }

            double t = currentSample / _sampleRate;

            // 波形計算 (Pad / Organ)
            double osc1 = 0;
            double osc2 = 0;
            double voiceGain = 0;
            double detuneGain = 0;

            switch (_tone)
            {
                case PlaybackTone.Pad:
                    osc1 = (_noteIndex == 0) ? Sawtooth(_phase1) : Triangle(_phase1);
                    osc2 = Sine(_phase2);
                    detuneGain = 0.018;

                    // Envelope: 0.0001 -> 0.09 at +0.18s -> 0.06 at +(dur*0.6)s -> 0 at +(dur+0.12)s
                    if (t < 0.18)
                        voiceGain = Lerp(0.0001, 0.09, t / 0.18);
                    else if (t < _durationSec * 0.6)
                        voiceGain = Lerp(0.09, 0.06, (t - 0.18) / Math.Max(0.01, _durationSec * 0.6 - 0.18));
                    else if (t < _durationSec + 0.12)
                        voiceGain = Lerp(0.06, 0.0001, (t - _durationSec * 0.6) / Math.Max(0.01, _durationSec + 0.12 - _durationSec * 0.6));
                    else
                        voiceGain = 0;
                    break;

                case PlaybackTone.Organ:
                    osc1 = Square(_phase1);
                    osc2 = Sine(_phase2);
                    detuneGain = 0.028;

                    // Envelope: 0.075 -> 0.075 at dur*0.82 -> 0.0001 at dur+0.04
                    if (t < _durationSec * 0.82)
                        voiceGain = 0.075;
                    else if (t < _durationSec + 0.04)
                        voiceGain = Lerp(0.075, 0.0001, (t - _durationSec * 0.82) / Math.Max(0.01, _durationSec + 0.04 - _durationSec * 0.82));
                    else
                        voiceGain = 0;
                    break;
            }

            // フェーズ更新 (事前計算した位相増分と高速な範囲ラップ)
            _phase1 += _dPhase1;
            if (_phase1 >= 1.0) _phase1 -= 1.0;

            _phase2 += _dPhase2;
            if (_phase2 >= 1.0) _phase2 -= 1.0;

            double mixed = (osc1 + osc2 * detuneGain) * voiceGain * _chordGainScale * 2.5;
            return (float)mixed;
        }

        private float NextPianoSample(long currentSample)
        {
            // 打弦アタック (2.5ms で急峻に立ち上がる)
            double attack = (currentSample < _attackSamples) ? ((double)currentSample / _attackSamples) : 1.0;

            // ダンパーリリース (durationSec 経過後の自然な弦のミュート減衰)
            double release = 1.0;
            if (currentSample > _releaseStartSamples)
            {
                long relSamples = currentSample - _releaseStartSamples;
                if (relSamples >= _releaseDurationSamples) return 0f;
                release = 1.0 - ((double)relSamples / _releaseDurationSamples);
            }

            // 各倍音の指数減衰（ホットパス内の Math.Exp を完全排除し、等比乗算で高速更新）
            double e1 = _e1; _e1 *= _decayMul1;
            double e2 = _e2; _e2 *= _decayMul2;
            double e3 = _e3; _e3 *= _decayMul3;
            double e4 = _e4; _e4 *= _decayMul4;
            double e5 = _e5; _e5 *= _decayMul5;
            double e6 = _e6; _e6 *= _decayMul6;
            double e7 = _e7; _e7 *= _decayMul7;

            // ハンマートランジェント (打鍵直後 10ms の木製フェルト打撃衝撃)
            double hammer = 0.0;
            if (currentSample < (long)(0.010 * _sampleRate))
            {
                double hEnv = _eHammer;
                _eHammer *= _decayMulHammer;
                hammer = hEnv * Math.Sin(_pHammer * (2.0 * Math.PI)) * 0.06;
                _pHammer += _dHammer;
                if (_pHammer >= 1.0) _pHammer -= 1.0;
            }

            // 各倍音の合成
            // 基音 + ユニゾン弦
            double harmonicMix = 1.00 * e1 * (Math.Sin(_p1 * (2.0 * Math.PI)) + 0.25 * Math.Sin(_pUnison1 * (2.0 * Math.PI)));

            // 第2倍音 + ユニゾン弦
            if (_d2 > 0)
                harmonicMix += 0.55 * e2 * (Math.Sin(_p2 * (2.0 * Math.PI)) + 0.15 * Math.Sin(_pUnison2 * (2.0 * Math.PI)));

            // 第3〜第7倍音
            if (_d3 > 0) harmonicMix += 0.30 * e3 * Math.Sin(_p3 * (2.0 * Math.PI));
            if (_d4 > 0) harmonicMix += 0.16 * e4 * Math.Sin(_p4 * (2.0 * Math.PI));
            if (_d5 > 0) harmonicMix += 0.09 * e5 * Math.Sin(_p5 * (2.0 * Math.PI));
            if (_d6 > 0) harmonicMix += 0.04 * e6 * Math.Sin(_p6 * (2.0 * Math.PI));
            if (_d7 > 0) harmonicMix += 0.02 * e7 * Math.Sin(_p7 * (2.0 * Math.PI));

            // フェーズ更新 (加算量は常に < 0.5 のため、FPU の Math.Floor を減算に置換)
            _p1 += _d1; if (_p1 >= 1.0) _p1 -= 1.0;
            _pUnison1 += _dUnison1; if (_pUnison1 >= 1.0) _pUnison1 -= 1.0;
            if (_d2 > 0) { _p2 += _d2; if (_p2 >= 1.0) _p2 -= 1.0; }
            if (_dUnison2 > 0) { _pUnison2 += _dUnison2; if (_pUnison2 >= 1.0) _pUnison2 -= 1.0; }
            if (_d3 > 0) { _p3 += _d3; if (_p3 >= 1.0) _p3 -= 1.0; }
            if (_d4 > 0) { _p4 += _d4; if (_p4 >= 1.0) _p4 -= 1.0; }
            if (_d5 > 0) { _p5 += _d5; if (_p5 >= 1.0) _p5 -= 1.0; }
            if (_d6 > 0) { _p6 += _d6; if (_p6 >= 1.0) _p6 -= 1.0; }
            if (_d7 > 0) { _p7 += _d7; if (_p7 >= 1.0) _p7 -= 1.0; }

            // 全体合成 (Pad/Organと同等のヘッドルームを確保し、和音発音時のクリッピング・音割れを防止)
            double normalizedHarmonics = harmonicMix * 0.36;
            double sample = (normalizedHarmonics + hammer) * attack * release;
            return (float)(sample * _chordGainScale * 0.35);
        }

        private static double Sine(double phase) => Math.Sin(phase * 2.0 * Math.PI);

        private static double Triangle(double phase) => (phase < 0.5) ? (4.0 * phase - 1.0) : (3.0 - 4.0 * phase);

        private static double Sawtooth(double phase) => 2.0 * phase - 1.0;

        private static double Square(double phase) => (phase < 0.5) ? 1.0 : -1.0;

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);
    }
}
