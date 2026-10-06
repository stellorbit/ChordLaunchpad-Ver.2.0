using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Tests;

public class SecurityTests
{
    // ==========================================
    // SecurityPathHelper Tests (パストラバーサル・予約名対策)
    // ==========================================

    [Theory]
    [InlineData(@"..\..\windows\system32\malicious.mid", "malicious.mid")]
    [InlineData(@"../../etc/passwd.mid", "passwd.mid")]
    [InlineData(@"C:\Windows\System32\cmd.exe.mid", "cmd.exe.mid")]
    [InlineData(@"subfolder/deep/test.mid", "test.mid")]
    public void SanitizeFileName_ShouldStripDirectoryTraversal(string input, string expected)
    {
        var sanitized = SecurityPathHelper.SanitizeFileName(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("CON.mid", "safe_CON.mid")]
    [InlineData("prn.mid", "safe_prn.mid")]
    [InlineData("aux.mid", "safe_aux.mid")]
    [InlineData("NUL.mid", "safe_NUL.mid")]
    [InlineData("COM1.mid", "safe_COM1.mid")]
    [InlineData("lpt9.mid", "safe_lpt9.mid")]
    public void SanitizeFileName_ShouldNeutralizeReservedDeviceNames(string input, string expected)
    {
        var sanitized = SecurityPathHelper.SanitizeFileName(input);
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void SanitizeFileName_ShouldReplaceInvalidCharsAndStripControlChars()
    {
        var input = "bad:file*name?with|invalid<chars>\0\r\n.mid";
        var sanitized = SecurityPathHelper.SanitizeFileName(input);

        Assert.DoesNotContain(":", sanitized);
        Assert.DoesNotContain("*", sanitized);
        Assert.DoesNotContain("?", sanitized);
        Assert.DoesNotContain("|", sanitized);
        Assert.DoesNotContain("<", sanitized);
        Assert.DoesNotContain(">", sanitized);
        Assert.False(sanitized.Contains('\0'));
        Assert.False(sanitized.Contains('\r'));
        Assert.False(sanitized.Contains('\n'));
        Assert.EndsWith(".mid", sanitized);
    }

    [Fact]
    public void SanitizeFileName_EmptyOrWhitespace_ReturnsDefaultName()
    {
        Assert.Equal("default.mid", SecurityPathHelper.SanitizeFileName("", "default.mid"));
        Assert.Equal("default.mid", SecurityPathHelper.SanitizeFileName("   ", "default.mid"));
        Assert.Equal("default.mid", SecurityPathHelper.SanitizeFileName(null, "default.mid"));
    }

    [Fact]
    public void SanitizeFileName_ExcessivelyLongName_TruncatesSafely()
    {
        var longBase = new string('a', 200);
        var input = $"{longBase}.mid";
        var sanitized = SecurityPathHelper.SanitizeFileName(input, maxLength: 64);

        Assert.True(sanitized.Length <= 64);
        Assert.EndsWith(".mid", sanitized);
    }

    [Theory]
    [InlineData("valid_file_name.mid", true)]
    [InlineData(@"..\evil.mid", false)]
    [InlineData(@"sub/evil.mid", false)]
    [InlineData("CON.mid", false)]
    [InlineData("AUX", false)]
    [InlineData("has:invalid.mid", false)]
    public void IsValidFileName_ShouldIdentifyDangerousNames(string input, bool expectedValid)
    {
        Assert.Equal(expectedValid, SecurityPathHelper.IsValidFileName(input));
    }

    // ==========================================
    // ProjectDataValidator Tests (境界値・DoS対策)
    // ==========================================

    [Fact]
    public void ValidateAndSanitize_NullProject_ReturnsSafeDefault()
    {
        var sanitized = ProjectDataValidator.ValidateAndSanitize(null);

        Assert.NotNull(sanitized);
        Assert.Equal("C", sanitized.Key);
        Assert.Equal(120, sanitized.Bpm);
        Assert.Empty(sanitized.Chords);
    }

    [Theory]
    [InlineData(-50, 20)]
    [InlineData(0, 20)]
    [InlineData(10, 20)]
    [InlineData(99999, 400)]
    [InlineData(120, 120)]
    [InlineData(200, 200)]
    public void ValidateAndSanitize_BpmClampedToValidRange(int inputBpm, int expectedBpm)
    {
        var raw = new ProjectData { Bpm = inputBpm };
        var sanitized = ProjectDataValidator.ValidateAndSanitize(raw);

        Assert.Equal(expectedBpm, sanitized.Bpm);
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("F#", "F#")]
    [InlineData("bb", "Bb")]      // 有効キーの小文字入力 "bb" は正規表記 "Bb" に正規化
    [InlineData("InvalidKey", "C")]
    [InlineData("XYZ", "C")]
    [InlineData(null, "C")]
    public void ValidateAndSanitize_KeyFallbackToSafeDefault(string? inputKey, string expectedKey)
    {
        var raw = new ProjectData { Key = inputKey! };
        var sanitized = ProjectDataValidator.ValidateAndSanitize(raw);

        Assert.Equal(expectedKey, sanitized.Key);
    }

    [Fact]
    public void ValidateAndSanitize_ExcessiveChordsCount_TruncatedToMaxLimit()
    {
        // 500件のコードを用意
        var manyChords = Enumerable.Range(0, 500)
            .Select(i => new ChordBlock { Symbol = $"C{i}", Root = "C" })
            .ToList();

        var raw = new ProjectData { Chords = manyChords };
        var sanitized = ProjectDataValidator.ValidateAndSanitize(raw);

        Assert.Equal(ProjectDataValidator.MaxChordsCount, sanitized.Chords.Count);
    }

    [Fact]
    public void ValidateAndSanitize_ExcessiveRawInputLength_Truncated()
    {
        var longInput = new string('A', 10000);
        var raw = new ProjectData { RawInput = longInput };
        var sanitized = ProjectDataValidator.ValidateAndSanitize(raw);

        Assert.Equal(ProjectDataValidator.MaxRawInputLength, sanitized.RawInput.Length);
    }

    [Fact]
    public void ValidateAndSanitize_SectionMarker_ClampsInsertIndex()
    {
        var chords = new List<ChordBlock>
        {
            new() { Symbol = "C", Root = "C" },
            new() { Symbol = "G", Root = "G" }
        };

        var markers = new List<SectionMarker>
        {
            new() { Name = "Valid", InsertIndex = 1 },
            new() { Name = "Negative", InsertIndex = -5 },
            new() { Name = "Overflow", InsertIndex = 999 }
        };

        var raw = new ProjectData { Chords = chords, Markers = markers };
        var sanitized = ProjectDataValidator.ValidateAndSanitize(raw);

        Assert.Equal(3, sanitized.Markers.Count);
        Assert.Equal(1, sanitized.Markers[0].InsertIndex);
        Assert.Equal(0, sanitized.Markers[1].InsertIndex); // 0 にクランプ
        Assert.Equal(2, sanitized.Markers[2].InsertIndex); // コード数 (2) にクランプ
    }

    // ==========================================
    // Atomic Write Tests (データ破壊・電源断耐性対策)
    // ==========================================

    [Fact]
    public void WriteAllTextAtomic_ShouldWriteNewFileCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ChordLaunchpadTest_{Guid.NewGuid():N}");
        try
        {
            var targetFile = Path.Combine(tempDir, "test_atomic.json");
            var content = "{\"hello\":\"world\"}";

            SecurityPathHelper.WriteAllTextAtomic(targetFile, content);

            Assert.True(File.Exists(targetFile));
            Assert.Equal(content, File.ReadAllText(targetFile));

            // 一時ファイルが残っていないことを検証
            var leftoverTmp = Directory.GetFiles(tempDir, "*.tmp_*");
            Assert.Empty(leftoverTmp);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_ShouldOverwriteExistingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ChordLaunchpadTest_{Guid.NewGuid():N}");
        try
        {
            var targetFile = Path.Combine(tempDir, "existing.json");
            SecurityPathHelper.WriteAllTextAtomic(targetFile, "old_content");

            var newContent = "new_replaced_content";
            SecurityPathHelper.WriteAllTextAtomic(targetFile, newContent);

            Assert.Equal(newContent, File.ReadAllText(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WriteAllTextAtomicAsync_ShouldWriteAndOverwriteCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ChordLaunchpadTest_{Guid.NewGuid():N}");
        try
        {
            var targetFile = Path.Combine(tempDir, "async_target.json");
            var content1 = "async_initial";
            var content2 = "async_overwritten";

            await SecurityPathHelper.WriteAllTextAtomicAsync(targetFile, content1);
            Assert.Equal(content1, await File.ReadAllTextAsync(targetFile));

            await SecurityPathHelper.WriteAllTextAtomicAsync(targetFile, content2);
            Assert.Equal(content2, await File.ReadAllTextAsync(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WriteAllTextAtomic_ShouldThrowOnInvalidPath(string? invalidPath)
    {
        Assert.Throws<ArgumentNullException>(() =>
            SecurityPathHelper.WriteAllTextAtomic(invalidPath!, "data"));
    }
}
