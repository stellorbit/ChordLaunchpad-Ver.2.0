using System;
using System.Collections.Generic;
using ChordLaunchpad.Audio;
using Xunit;

namespace ChordLaunchpad.Tests;

public class ToneSynthesizerTests
{
    [Fact]
    public void TestPlaybackToneEnumValues()
    {
        var names = Enum.GetNames<PlaybackTone>();
        Assert.Equal(3, names.Length);
        Assert.Contains("Piano", names);
        Assert.Contains("Pad", names);
        Assert.Contains("Organ", names);
        Assert.DoesNotContain("Pluck", names);
    }

    [Theory]
    [InlineData(PlaybackTone.Piano)]
    [InlineData(PlaybackTone.Pad)]
    [InlineData(PlaybackTone.Organ)]
    public void TestTriggerChordAndReadSamples(PlaybackTone tone)
    {
        var synth = new ToneSynthesizer(44100);
        // C4, E4, G4, B4 (Cmaj7)
        var notes = new List<int> { 60, 64, 67, 71 };
        synth.TriggerChord(notes, 500.0, tone);

        float[] buffer = new float[1024];
        int read = synth.Read(buffer.AsSpan());

        Assert.Equal(1024, read);

        bool hasNonZero = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            float s = buffer[i];
            Assert.False(float.IsNaN(s), $"Sample at index {i} was NaN for tone {tone}");
            Assert.False(float.IsInfinity(s), $"Sample at index {i} was Infinity for tone {tone}");
            Assert.InRange(s, -1.0f, 1.0f);
            if (Math.Abs(s) > 0.0001f)
            {
                hasNonZero = true;
            }
        }

        Assert.True(hasNonZero, $"Tone {tone} produced all-zero samples immediately after triggering chord");
    }

    [Fact]
    public void TestStopAllClearsVoices()
    {
        var synth = new ToneSynthesizer(44100);
        synth.TriggerChord(new[] { 60, 64, 67 }, 1000.0, PlaybackTone.Piano);

        synth.StopAll();

        float[] buffer = new float[512];
        synth.Read(buffer.AsSpan());

        foreach (var s in buffer)
        {
            Assert.Equal(0f, s);
        }
    }

    [Fact]
    public void TestPianoChordNoClippingOrDistortion()
    {
        var synth = new ToneSynthesizer(44100);
        // 8音の密集・重低音和音 (C2, G2, C3, E3, G3, B3, D4, F#4: Cmaj9#11)
        var heavyChord = new List<int> { 36, 43, 48, 52, 55, 59, 62, 66 };
        synth.TriggerChord(heavyChord, 1000.0, PlaybackTone.Piano);

        // 最初の 0.5 秒 (22050 サンプル) を読み出して検証
        float[] buffer = new float[2048];
        float maxPeak = 0f;

        for (int chunk = 0; chunk < 10; chunk++)
        {
            synth.Read(buffer.AsSpan());
            for (int i = 0; i < buffer.Length; i++)
            {
                float s = buffer[i];
                Assert.False(float.IsNaN(s));
                Assert.False(float.IsInfinity(s));
                Assert.InRange(s, -1.0f, 1.0f);
                float abs = Math.Abs(s);
                if (abs > maxPeak)
                {
                    maxPeak = abs;
                }
            }
        }

        // 音割れ防止: ピークが 0 より大きく、1.0f 以下で正しく発音されていること
        Assert.True(maxPeak > 0.1f, "Piano sound is too quiet");
        Assert.True(maxPeak <= 1.0f, "Piano sound exceeded 1.0f (clipped)");
    }
}
