using System.Collections.Generic;
using System.Linq;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;
using ChordLaunchpad.Export;
using Xunit;

namespace ChordLaunchpad.Tests;

public class MusicEngineTests
{
    [Fact]
    public void TestParseRomanNumerals_ClassicPop()
    {
        // "I V vi IV" in C Major -> C, G, Am, F
        var result = MusicEngine.ParseProgression("I V vi IV", "C", MusicalMode.Major, "1 bar");

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Chords.Count);
        Assert.Equal("C", result.Chords[0].Symbol);
        Assert.Equal("G", result.Chords[1].Symbol);
        Assert.Equal("Am", result.Chords[2].Symbol);
        Assert.Equal("F", result.Chords[3].Symbol);
    }

    [Fact]
    public void TestParseArabicCommands_ScreenshotExample()
    {
        // 4mas 3svn 6mis 1svn in C Major -> Fmaj7, E7, Am7, C7
        var result = MusicEngine.ParseProgression("4mas 3svn 6mis 1svn", "C", MusicalMode.Major, "1 bar");

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Chords.Count);

        Assert.Equal("Fmaj7", result.Chords[0].Symbol);
        Assert.Equal("IVmaj7", result.Chords[0].RomanNumeral);

        Assert.Equal("E7", result.Chords[1].Symbol);
        Assert.Equal("iii7", result.Chords[1].RomanNumeral);

        Assert.Equal("Am7", result.Chords[2].Symbol);
        Assert.Equal("vi7", result.Chords[2].RomanNumeral);

        Assert.Equal("C7", result.Chords[3].Symbol);
        Assert.Equal("I7", result.Chords[3].RomanNumeral);
    }

    [Fact]
    public void TestParseStructuredInput_WithBarsAndTies()
    {
        // "Fmaj7 - - - | E7 - - - | Am7 - - - | C7 - - -"
        var input = "Fmaj7 - - - | E7 - - - | Am7 - - - | C7 - - -";
        var result = MusicEngine.ParseProgression(input, "C", MusicalMode.Major, "1 bar", "4/4", 4);

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Chords.Count);

        Assert.Equal("Fmaj7", result.Chords[0].Symbol);
        Assert.Equal("1 bar", result.Chords[0].Duration);

        Assert.Equal("E7", result.Chords[1].Symbol);
        Assert.Equal("1 bar", result.Chords[1].Duration);

        Assert.Equal("Am7", result.Chords[2].Symbol);
        Assert.Equal("1 bar", result.Chords[2].Duration);

        Assert.Equal("C7", result.Chords[3].Symbol);
        Assert.Equal("1 bar", result.Chords[3].Duration);
    }

    [Fact]
    public void TestSlashChord_RelativeBass()
    {
        // G/B in C Major
        var result = MusicEngine.ParseProgression("G/B", "C", MusicalMode.Major, "1 bar");

        Assert.Empty(result.Errors);
        var chord = result.Chords[0];
        Assert.Equal("G/B", chord.Symbol);
        Assert.Equal("G", chord.Root);
        Assert.Equal("B", chord.Bass);
    }

    [Fact]
    public void TestMidiNoteNumbers_WithBassAddition()
    {
        // C Major: RootBase 60 -> C4(60), E4(64), G4(67)
        var result = MusicEngine.ParseProgression("C", "C", MusicalMode.Major, "1 bar");
        var chord = result.Chords[0];

        var basicNotes = MusicEngine.MidiNoteNumbers(chord, BassAdditionMode.None);
        Assert.Equal(new[] { 60, 64, 67 }, basicNotes);

        // Bass addition "Both": 24 + 0 = 24 (C1), 36 + 0 = 36 (C2)
        var bassBoth = MusicEngine.MidiNoteNumbers(chord, BassAdditionMode.Both);
        Assert.Contains(24, bassBoth);
        Assert.Contains(36, bassBoth);
        Assert.Contains(60, bassBoth);
        Assert.Contains(64, bassBoth);
        Assert.Contains(67, bassBoth);
    }

    [Fact]
    public void TestMidiBuilder_GeneratesValidHeader()
    {
        var result = MusicEngine.ParseProgression("I V vi IV", "C", MusicalMode.Major, "1 bar");
        var bytes = MidiBuilder.BuildMidiBytes(result.Chords, 120, "4/4");

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 14);

        // Check MThd
        Assert.Equal(0x4d, bytes[0]); // 'M'
        Assert.Equal(0x54, bytes[1]); // 'T'
        Assert.Equal(0x68, bytes[2]); // 'h'
        Assert.Equal(0x64, bytes[3]); // 'd'
    }

    [Fact]
    public void TestParseRomanWithAccidentals_ModalInterchange()
    {
        // "Imaj7 IVmaj7 bVII7 Imaj7" in C Major -> Cmaj7, Fmaj7, Bb7, Cmaj7
        var result = MusicEngine.ParseProgression("Imaj7 IVmaj7 bVII7 Imaj7", "C", MusicalMode.Major, "1 bar");

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Chords.Count);
        Assert.Equal("Cmaj7", result.Chords[0].Symbol);
        Assert.Equal("Fmaj7", result.Chords[1].Symbol);
        Assert.Equal("Bb7", result.Chords[2].Symbol);
        Assert.Equal("Cmaj7", result.Chords[3].Symbol);

        // Subdominant minor: iv -> Fm
        var ivResult = MusicEngine.ParseProgression("iv", "C", MusicalMode.Major, "1 bar");
        Assert.Empty(ivResult.Errors);
        Assert.Equal("Fm", ivResult.Chords[0].Symbol);
    }

    [Fact]
    public void TestAllDefaultTemplates_ParseSuccessfully()
    {
        foreach (var template in DefaultTemplates.Templates)
        {
            var result = MusicEngine.ParseProgression(template.Progression, "C", MusicalMode.Major, "1 bar");
            Assert.True(result.Errors.Count == 0, $"Template '{template.Name}' failed to parse: {string.Join(", ", result.Errors.Select(e => $"{e.Token}: {e.Message}"))}");
            Assert.True(result.Chords.Count > 0, $"Template '{template.Name}' resulted in 0 chords");
        }
    }

    [Theory]
    [InlineData("1/16", 0.25)]
    [InlineData("1/8", 0.5)]
    [InlineData("3/8", 1.5)]
    [InlineData("5/8", 2.5)]
    [InlineData("7/8", 3.5)]
    [InlineData("1 beat", 1.0)]
    [InlineData("2 beats", 2.0)]
    [InlineData("3 beats", 3.0)]
    [InlineData("1 bar", 4.0)]
    [InlineData("2 bars", 8.0)]
    [InlineData("0.5 beats", 0.5)]
    [InlineData("3.5 beats", 3.5)]
    public void TestChordDurationBeats_VariousGridAndSyncopations(string duration, double expectedBeats)
    {
        var beats = MusicEngine.ChordDurationBeats(duration, "4/4");
        Assert.Equal(expectedBeats, beats, precision: 3);
    }

    [Theory]
    [InlineData(0.25, "1/16")]
    [InlineData(0.5, "1/8")]
    [InlineData(1.0, "1 beat")]
    [InlineData(1.5, "3/8")]
    [InlineData(2.0, "2 beats")]
    [InlineData(3.5, "7/8")]
    [InlineData(4.0, "1 bar")]
    [InlineData(8.0, "2 bars")]
    public void TestBeatsToDuration_Conversions(double beats, string expectedDuration)
    {
        var duration = MusicEngine.BeatsToDuration(beats, "4/4");
        Assert.Equal(expectedDuration, duration);
    }

    [Theory]
    [InlineData("1 bar", false, "1小節")]
    [InlineData("2 bars", false, "2小節")]
    [InlineData("1 beat", false, "1拍")]
    [InlineData("2 beats", false, "2拍")]
    [InlineData("1/8", false, "0.5拍")]
    [InlineData("3/8", false, "1.5拍")]
    [InlineData("7/8", false, "3.5拍")]
    [InlineData("1/16", false, "0.25拍")]
    public void TestDurationLabel_JapaneseFormatting(string duration, bool dotted, string expectedLabel)
    {
        var label = MusicEngine.DurationLabel(duration, dotted);
        Assert.Equal(expectedLabel, label);
    }
}
