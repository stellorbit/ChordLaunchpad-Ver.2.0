using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ChordLaunchpad.Audio;

/// <summary>
/// WDM (WASAPI 共有モード) を使用してDAWと競合せずに音声再生を行うオーディオエンジン
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private static readonly Lazy<AudioEngine> _instance = new(() => new AudioEngine());
    public static AudioEngine Instance => _instance.Value;

    private readonly IWavePlayer? _player;
    private readonly ToneSynthesizer _synthesizer;
    private bool _isDisposed;

    public AudioEngine(int sampleRate = 44100, int latencyMs = 50)
    {
        _synthesizer = new ToneSynthesizer(sampleRate);

        try
        {
            // WASAPI Shared (共有モード) で初期化。DAWのASIOや他アプリの再生と干渉せず、Windowsミキサーで共存
            _player = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithLatency(latencyMs)
                .Build();

            _player.Init(new SampleToWaveProvider(_synthesizer));
            _player.Play();
        }
        catch
        {
            // オーディオ出力デバイスが一時的に利用不能な場合でもクラッシュを防止
            _player = null;
        }
    }

    /// <summary>
    /// 和音または単音を指定した長さ・音色でプレビュー再生
    /// </summary>
    public void PlayNotes(IReadOnlyList<int> midiNotes, double durationMs, PlaybackTone tone)
    {
        if (_isDisposed) return;
        _synthesizer.TriggerChord(midiNotes, durationMs, tone);
    }

    /// <summary>
    /// 発音中の音を全停止
    /// </summary>
    public void StopAll()
    {
        if (_isDisposed) return;
        _synthesizer.StopAll();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _player?.Stop();
            _player?.Dispose();
        }
        catch
        {
            // シャットダウン時の例外抑制
        }
    }
}
