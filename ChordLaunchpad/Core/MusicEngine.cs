using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Core;

public static partial class MusicEngine
{
    private static readonly string[] SharpNotes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNotes = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    public static readonly string[] KeyCandidates = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    private static readonly int[] DegreeOffsetsMajor = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] DegreeOffsetsMinor = [0, 2, 3, 5, 7, 8, 10];
    private static readonly int[] MajorDegreeLookup = [0, -1, 1, -1, 2, 3, -1, 4, -1, 5, -1, 6];
    private static readonly int[] MinorDegreeLookup = [0, -1, 1, 2, -1, 3, -1, 4, 5, -1, 6, -1];

    private static readonly Dictionary<string, int> RomanToDegreeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = 0,
        ["ii"] = 1,
        ["iii"] = 2,
        ["iv"] = 3,
        ["v"] = 4,
        ["vi"] = 5,
        ["vii"] = 6
    };

    public static readonly string[] DegreeLabelsMajor = ["I", "ii", "iii", "IV", "V", "vi", "viidim"];
    public static readonly string[] DegreeLabelsMinor = ["i", "iidim", "III", "iv", "v", "VI", "VII"];

    private static readonly Dictionary<ChordQuality, int[]> QualityIntervals = new()
    {
        [ChordQuality.Rest] = [],
        [ChordQuality.Major] = [0, 4, 7],
        [ChordQuality.Minor] = [0, 3, 7],
        [ChordQuality.Diminished] = [0, 3, 6],
        [ChordQuality.HalfDiminished] = [0, 3, 6, 10],
        [ChordQuality.Augmented] = [0, 4, 8],
        [ChordQuality.Dominant7] = [0, 4, 7, 10],
        [ChordQuality.Major7] = [0, 4, 7, 11],
        [ChordQuality.Minor7] = [0, 3, 7, 10],
        [ChordQuality.Add9] = [0, 4, 7, 14],
        [ChordQuality.MinorAdd9] = [0, 3, 7, 14],
        [ChordQuality.Sus2] = [0, 2, 7],
        [ChordQuality.Sixth] = [0, 4, 7, 9],
        [ChordQuality.Minor6] = [0, 3, 7, 9],
        [ChordQuality.Sus4] = [0, 5, 7]
    };

    private record ExtendedDescriptor(ChordQuality Quality, int[] Intervals, string Descriptor);

    private static readonly Dictionary<string, ExtendedDescriptor> ExtendedDescriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dim7"] = new(ChordQuality.Diminished, [0, 3, 6, 9], "dim7"),
        ["9"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 14], "9"),
        ["maj9"] = new(ChordQuality.Major7, [0, 4, 7, 11, 14], "maj9"),
        ["m9"] = new(ChordQuality.Minor7, [0, 3, 7, 10, 14], "m9"),
        ["11"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 14, 17], "11"),
        ["m11"] = new(ChordQuality.Minor7, [0, 3, 7, 10, 14, 17], "m11"),
        ["13"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 14, 17, 21], "13"),
        ["m13"] = new(ChordQuality.Minor7, [0, 3, 7, 10, 14, 17, 21], "m13"),
        ["add11"] = new(ChordQuality.Major, [0, 4, 7, 17], "add11"),
        ["madd11"] = new(ChordQuality.Minor, [0, 3, 7, 17], "madd11"),
        ["add13"] = new(ChordQuality.Major, [0, 4, 7, 21], "add13"),
        ["madd13"] = new(ChordQuality.Minor, [0, 3, 7, 21], "madd13"),
        ["7sus4"] = new(ChordQuality.Sus4, [0, 5, 7, 10], "7sus4"),
        ["7b9"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 13], "7b9"),
        ["7#9"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 15], "7#9"),
        ["7#11"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 18], "7#11"),
        ["7b13"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 20], "7b13"),
        ["alt"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 13, 15], "alt"),
        ["7alt"] = new(ChordQuality.Dominant7, [0, 4, 7, 10, 13, 15], "7alt")
    };

    [GeneratedRegex(@"^([A-Ga-g])([#b]?)$")]
    private static partial Regex NoteRegex();

    [GeneratedRegex(@"^([b♭#♯]?)([ivIV]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RomanTokenRegex();

    [GeneratedRegex(@"maj7|m7b5|halfdim|min|m(?!aj)|add9|sus2|sus4|aug|6|9|7|dim|ø", RegexOptions.IgnoreCase)]
    private static partial Regex DegreeCleanRegex();

    [GeneratedRegex(@"^[b♭#♯]?[iv]+m")]
    private static partial Regex MinorMarkedRegex1();

    [GeneratedRegex(@"^[b♭#♯]?[iv]+min")]
    private static partial Regex MinorMarkedRegex2();

    [GeneratedRegex(@"^vii(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ViiRegex();

    [GeneratedRegex(@"^[b♭#♯]?([ivIV]+)")]
    private static partial Regex RomanBaseRegex();

    [GeneratedRegex(@"^(r|rest|nc|n\.c\.|休符)$", RegexOptions.IgnoreCase)]
    private static partial Regex RestTokenRegex();

    [GeneratedRegex(@"^([1-7])(m75|hdm|mis|mas|svn|adn|sut|suf|m7|M7|maj7|dim|aug|sus2|sus4|add9|m|M|7|6|six)?$")]
    private static partial Regex ArabicDegreeRegex();

    [GeneratedRegex(@"^([A-G])([#b]?)([^/\s]*)?(?:/([A-G][#b]?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChordSymbolRegex();

    [GeneratedRegex(@"^(.*?)(?:\(|（)\s*(\d+)/8\s*(?:\)|）)(\.)?$")]
    private static partial Regex ExplicitDurationTokenRegex();

    [GeneratedRegex(@"[|\n]")]
    private static partial Regex BarSeparatorCheckRegex();

    [GeneratedRegex(@"[|\n]+")]
    private static partial Regex BarSplitRegex();

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex WhitespaceOrCommaSplitRegex();

    [GeneratedRegex(@"^(\d+)/8$")]
    private static partial Regex DurationEighthRegex();

    [GeneratedRegex(@"^(\d+)/16$")]
    private static partial Regex DurationSixteenthRegex();

    [GeneratedRegex(@"^([\d\.]+)\s*beats?$", RegexOptions.IgnoreCase)]
    private static partial Regex BeatsDurationRegex();

    [GeneratedRegex(@"^([\d\.]+)\s*bars?$", RegexOptions.IgnoreCase)]
    private static partial Regex BarsDurationRegex();

    public static string? NormalizeNote(string note)
    {
        var trimmed = note.Trim();
        var match = NoteRegex().Match(trimmed);
        if (!match.Success) return null;

        var normalized = $"{match.Groups[1].Value.ToUpper()}{match.Groups[2].Value}";
        if (SharpNotes.Contains(normalized) || FlatNotes.Contains(normalized))
        {
            return normalized;
        }
        return null;
    }

    public static int NoteIndex(string note)
    {
        var normalized = NormalizeNote(note);
        if (normalized == null) return -1;
        var sharpIndex = Array.IndexOf(SharpNotes, normalized);
        if (sharpIndex >= 0) return sharpIndex;
        return Array.IndexOf(FlatNotes, normalized);
    }

    public static string NoteAt(int index, bool preferFlats = false)
    {
        var normalizedIndex = ((index % 12) + 12) % 12;
        return preferFlats ? FlatNotes[normalizedIndex] : SharpNotes[normalizedIndex];
    }

    public static bool PreferFlatsForSignature(string key, MusicalMode mode)
    {
        var flatMajorKeys = new HashSet<string> { "F", "Bb", "Eb", "Ab", "Db", "Gb", "Cb" };
        var flatMinorKeys = new HashSet<string> { "D", "G", "C", "F", "Bb", "Eb", "Ab" };
        return mode == MusicalMode.Major ? flatMajorKeys.Contains(key) : flatMinorKeys.Contains(key);
    }

    public static List<string> BuildChordNotes(string root, ChordQuality quality)
    {
        if (quality == ChordQuality.Rest) return new List<string>();
        var @base = NoteIndex(root);
        var preferFlats = root.Contains('b');
        return QualityIntervals[quality].Select(interval => NoteAt(@base + interval, preferFlats)).ToList();
    }

    public static List<string> BuildNotesFromIntervals(string root, IReadOnlyList<int> intervals)
    {
        var @base = NoteIndex(root);
        var preferFlats = root.Contains('b');
        return intervals.Select(interval => NoteAt(@base + interval, preferFlats)).ToList();
    }

    public static List<string> RotateNotes(IReadOnlyList<string> notes, StylePreset inversion = StylePreset.Root)
    {
        if (notes.Count <= 1 || inversion == StylePreset.Root)
        {
            return notes.ToList();
        }

        var steps = inversion == StylePreset.First ? 1 : inversion == StylePreset.Second ? 2 : 3;
        var safeSteps = Math.Min(steps, Math.Max(0, notes.Count - 1));
        return notes.Skip(safeSteps).Concat(notes.Take(safeSteps)).ToList();
    }

    public static List<string> WithBass(IReadOnlyList<string> notes, string? bass)
    {
        if (string.IsNullOrEmpty(bass) || (notes.Count > 0 && bass == notes[0]))
        {
            return notes.ToList();
        }
        return [bass, .. notes];
    }

    public static int? RelativeBass(string root, string? bass)
    {
        if (string.IsNullOrEmpty(bass)) return null;
        var rootIndex = NoteIndex(root);
        var bassIndex = NoteIndex(bass);
        if (rootIndex < 0 || bassIndex < 0) return null;
        return (bassIndex - rootIndex + 12) % 12;
    }

    public static string? RebasedBass(string nextRoot, string originalRoot, string? originalBass)
    {
        var offset = RelativeBass(originalRoot, originalBass);
        if (offset == null) return null;
        return NoteAt(NoteIndex(nextRoot) + offset.Value, nextRoot.Contains('b'));
    }

    public static (int? Degree, int Accidental) ParseRomanDegreeInfo(string token)
    {
        var match = RomanTokenRegex().Match(token.Trim());
        if (!match.Success) return (null, 0);

        var accStr = match.Groups[1].Value;
        var romanBase = match.Groups[2].Value.ToLower();

        var accidental = 0;
        if (accStr == "b" || accStr == "♭") accidental = -1;
        else if (accStr == "#" || accStr == "♯") accidental = 1;

        if (RomanToDegreeMap.TryGetValue(romanBase, out var deg))
        {
            return (deg, accidental);
        }

        return (null, 0);
    }

    public static string NormalizeRomanToken(string token)
    {
        return DegreeCleanRegex().Replace(token, "").ToLower();
    }

    public static int? RomanDegree(string token)
    {
        var info = ParseRomanDegreeInfo(token);
        return info.Degree;
    }

    public static ChordQuality RomanQuality(string token, MusicalMode mode)
    {
        var hasSeven = token.Contains('7');
        var lower = token.ToLower();
        var minorMarked = MinorMarkedRegex1().IsMatch(lower) || MinorMarkedRegex2().IsMatch(lower);
        if (lower.Contains("maj7")) return ChordQuality.Major7;
        if (lower.Contains("ø") || lower.Contains("m7b5") || lower.Contains("halfdim")) return ChordQuality.HalfDiminished;
        if (lower.Contains("aug")) return ChordQuality.Augmented;
        if (lower.Contains("sus2")) return ChordQuality.Sus2;
        if (lower.Contains("sus4")) return ChordQuality.Sus4;
        if (lower.Contains("add9")) return minorMarked ? ChordQuality.MinorAdd9 : ChordQuality.Add9;
        if (lower.Contains("6")) return minorMarked ? ChordQuality.Minor6 : ChordQuality.Sixth;
        if (lower.Contains("dim") || (!lower.StartsWith("b") && !lower.StartsWith("♭") && ViiRegex().IsMatch(lower))) return ChordQuality.Diminished;
        if (hasSeven)
        {
            if (minorMarked) return ChordQuality.Minor7;
            var romanBase = RomanBaseRegex().Match(token).Groups[1].Value;
            if (!string.IsNullOrEmpty(romanBase) && romanBase == romanBase.ToUpper()) return ChordQuality.Dominant7;
            return ChordQuality.Minor7;
        }
        if (mode == MusicalMode.Minor && lower == "v") return ChordQuality.Minor;
        if (minorMarked) return ChordQuality.Minor;
        var romanOnly = RomanBaseRegex().Match(token).Groups[1].Value;
        return (!string.IsNullOrEmpty(romanOnly) && romanOnly == romanOnly.ToUpper()) ? ChordQuality.Major : ChordQuality.Minor;
    }

    public static string ChordSymbol(string root, ChordQuality quality, string? descriptor = null, string? bass = null)
    {
        if (quality == ChordQuality.Rest) return "R";
        var suffix = !string.IsNullOrEmpty(bass) ? $"/{bass}" : "";
        if (!string.IsNullOrEmpty(descriptor))
        {
            return $"{root}{descriptor}{suffix}";
        }

        return quality switch
        {
            ChordQuality.Minor => $"{root}m{suffix}",
            ChordQuality.Diminished => $"{root}dim{suffix}",
            ChordQuality.HalfDiminished => $"{root}m7b5{suffix}",
            ChordQuality.Augmented => $"{root}aug{suffix}",
            ChordQuality.Dominant7 => $"{root}7{suffix}",
            ChordQuality.Major7 => $"{root}maj7{suffix}",
            ChordQuality.Minor7 => $"{root}m7{suffix}",
            ChordQuality.Add9 => $"{root}add9{suffix}",
            ChordQuality.MinorAdd9 => $"{root}madd9{suffix}",
            ChordQuality.Sus2 => $"{root}sus2{suffix}",
            ChordQuality.Sixth => $"{root}6{suffix}",
            ChordQuality.Minor6 => $"{root}m6{suffix}",
            ChordQuality.Sus4 => $"{root}sus4{suffix}",
            _ => $"{root}{suffix}"
        };
    }

    public static bool IsRestToken(string token)
    {
        return RestTokenRegex().IsMatch(token.Trim());
    }

    public static ChordBlock CreateRestBlock(string source = "symbol")
    {
        return new ChordBlock
        {
            Symbol = "R",
            Root = "C",
            Inversion = StylePreset.Root,
            Quality = ChordQuality.Rest,
            RomanNumeral = "休符",
            Notes = Array.Empty<string>(),
            Source = source
        };
    }

    public static (ChordQuality Quality, string? Descriptor, int[]? Intervals) ParseDescriptor(string descriptor)
    {
        var normalized = descriptor.ToLower();
        if (ExtendedDescriptors.TryGetValue(normalized, out var entry))
        {
            return (entry.Quality, entry.Descriptor, entry.Intervals);
        }

        return (DetectQuality(normalized), null, null);
    }

    public static ChordQuality DetectQuality(string descriptor)
    {
        return descriptor switch
        {
            "m" or "min" => ChordQuality.Minor,
            "ø" or "ø7" or "m7b5" or "halfdim" => ChordQuality.HalfDiminished,
            "aug" or "+" => ChordQuality.Augmented,
            "7" => ChordQuality.Dominant7,
            "maj7" => ChordQuality.Major7,
            "m7" or "min7" => ChordQuality.Minor7,
            "add9" or "add2" => ChordQuality.Add9,
            "madd9" or "m(add9)" => ChordQuality.MinorAdd9,
            "sus2" => ChordQuality.Sus2,
            "6" => ChordQuality.Sixth,
            "m6" => ChordQuality.Minor6,
            "dim" => ChordQuality.Diminished,
            "sus4" => ChordQuality.Sus4,
            _ => ChordQuality.Major
        };
    }

    public static ChordBlock? RomanToChordSymbol(string roman, string key, MusicalMode mode)
    {
        var (degree, accidental) = ParseRomanDegreeInfo(roman);
        if (degree == null) return null;
        var keyIndex = NoteIndex(key);
        if (keyIndex < 0) return null;
        var intervals = mode == MusicalMode.Major ? DegreeOffsetsMajor : DegreeOffsetsMinor;
        var preferFlats = accidental < 0 || PreferFlatsForSignature(key, mode);
        var root = NoteAt(keyIndex + intervals[degree.Value] + accidental, preferFlats);
        var quality = RomanQuality(roman, mode);

        return new ChordBlock
        {
            Symbol = ChordSymbol(root, quality),
            Root = root,
            Inversion = StylePreset.Root,
            Quality = quality,
            RomanNumeral = roman,
            Notes = RotateNotes(BuildChordNotes(root, quality), StylePreset.Root),
            Source = "roman"
        };
    }

    public static ChordBlock? ArabicDegreeToChordSymbol(string token, string key, MusicalMode mode)
    {
        var match = ArabicDegreeRegex().Match(token);
        if (!match.Success) return null;

        var degree = int.Parse(match.Groups[1].Value) - 1;
        var keyIndex = NoteIndex(key);
        if (degree < 0 || degree > 6 || keyIndex < 0) return null;

        var intervals = mode == MusicalMode.Major ? DegreeOffsetsMajor : DegreeOffsetsMinor;
        ChordQuality[] diatonicQualities = mode == MusicalMode.Major
            ? [ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Diminished]
            : [ChordQuality.Minor, ChordQuality.Diminished, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major];

        var root = NoteAt(keyIndex + intervals[degree], PreferFlatsForSignature(key, mode));
        var suffix = match.Groups[2].Value;
        var quality = diatonicQualities[degree];
        string? descriptor = null;
        int[]? customIntervals = null;

        switch (suffix)
        {
            case "":
                descriptor = quality == ChordQuality.HalfDiminished ? "m7b5" : null;
                break;
            case "m":
            case "min":
                quality = ChordQuality.Minor;
                break;
            case "M":
            case "maj":
                quality = ChordQuality.Major;
                break;
            case "dim":
                quality = ChordQuality.Diminished;
                descriptor = "dim";
                break;
            case "aug":
                quality = ChordQuality.Augmented;
                descriptor = "aug";
                break;
            case "7":
            case "svn":
                quality = quality == ChordQuality.Minor ? ChordQuality.Minor7 : ChordQuality.Dominant7;
                descriptor = "7";
                break;
            case "m7":
            case "mis":
                quality = ChordQuality.Minor7;
                descriptor = "m7";
                break;
            case "M7":
            case "maj7":
            case "mas":
                quality = ChordQuality.Major7;
                descriptor = "maj7";
                break;
            case "sus2":
            case "sut":
                quality = ChordQuality.Sus2;
                descriptor = "sus2";
                break;
            case "sus4":
            case "suf":
                quality = ChordQuality.Sus4;
                descriptor = "sus4";
                break;
            case "add9":
            case "adn":
                quality = quality == ChordQuality.Minor ? ChordQuality.MinorAdd9 : ChordQuality.Add9;
                descriptor = quality == ChordQuality.MinorAdd9 ? "madd9" : "add9";
                break;
            case "6":
            case "six":
                quality = quality == ChordQuality.Minor ? ChordQuality.Minor6 : ChordQuality.Sixth;
                descriptor = quality == ChordQuality.Minor6 ? "m6" : "6";
                break;
            case "m75":
            case "hdm":
                quality = ChordQuality.HalfDiminished;
                descriptor = "m7b5";
                customIntervals = QualityIntervals[ChordQuality.HalfDiminished];
                break;
            default:
                return null;
        }

        var chordNotes = customIntervals != null
            ? BuildNotesFromIntervals(root, customIntervals)
            : BuildChordNotes(root, quality);

        return new ChordBlock
        {
            Symbol = ChordSymbol(root, quality, descriptor),
            Root = root,
            Inversion = StylePreset.Root,
            Quality = quality,
            Descriptor = descriptor,
            Intervals = customIntervals,
            RomanNumeral = ChordToRoman(root, quality, key, mode),
            Notes = RotateNotes(chordNotes, StylePreset.Root),
            Source = "roman"
        };
    }

    public static string ChordToRoman(string root, ChordQuality quality, string key, MusicalMode mode)
    {
        if (quality == ChordQuality.Rest) return "休符";
        var keyIndex = NoteIndex(key);
        var rootIndex = NoteIndex(root);
        if (keyIndex < 0 || rootIndex < 0) return "?";
        var distance = (rootIndex - keyIndex + 12) % 12;
        var degrees = mode == MusicalMode.Major ? DegreeOffsetsMajor : DegreeOffsetsMinor;
        var labels = mode == MusicalMode.Major ? DegreeLabelsMajor : DegreeLabelsMinor;

        var degree = Array.IndexOf(degrees, distance);
        if (degree == -1) return "?";
        var @base = labels[degree].Replace("dim", "");
        var upperBase = @base.ToUpper();
        var lowerBase = @base.ToLower();

        return quality switch
        {
            ChordQuality.Major => upperBase,
            ChordQuality.Minor => lowerBase,
            ChordQuality.Diminished => $"{lowerBase}dim",
            ChordQuality.HalfDiminished => $"{lowerBase}ø7",
            ChordQuality.Augmented => $"{upperBase}aug",
            ChordQuality.Dominant7 => $"{upperBase}7",
            ChordQuality.Major7 => $"{upperBase}maj7",
            ChordQuality.Minor7 => $"{lowerBase}7",
            ChordQuality.Add9 => $"{upperBase}add9",
            ChordQuality.MinorAdd9 => $"{lowerBase}add9",
            ChordQuality.Sus2 => $"{upperBase}sus2",
            ChordQuality.Sixth => $"{upperBase}6",
            ChordQuality.Minor6 => $"{lowerBase}6",
            ChordQuality.Sus4 => $"{upperBase}sus4",
            _ => upperBase
        };
    }

    public static ChordBlock? ParseSymbolToken(string token, string key, MusicalMode mode)
    {
        if (IsRestToken(token))
        {
            return CreateRestBlock("symbol");
        }

        var match = ChordSymbolRegex().Match(token);
        if (!match.Success) return null;

        var root = NormalizeNote($"{match.Groups[1].Value.ToUpper()}{match.Groups[2].Value}");
        if (root == null) return null;

        string? bass = null;
        if (match.Groups[4].Success && !string.IsNullOrEmpty(match.Groups[4].Value))
        {
            bass = NormalizeNote(match.Groups[4].Value);
            if (bass == null) return null;
        }

        var descriptor = match.Groups[3].Value.ToLower();
        var (quality, descSuffix, intervals) = ParseDescriptor(descriptor);
        var finalDesc = descSuffix ?? (!string.IsNullOrEmpty(descriptor) ? descriptor : null);

        var chordNotes = intervals != null
            ? BuildNotesFromIntervals(root, intervals)
            : BuildChordNotes(root, quality);

        return new ChordBlock
        {
            Symbol = ChordSymbol(root, quality, finalDesc, bass),
            Root = root,
            Bass = bass,
            Inversion = StylePreset.Root,
            Quality = quality,
            Descriptor = finalDesc,
            Intervals = intervals,
            RomanNumeral = ChordToRoman(root, quality, key, mode),
            Notes = WithBass(RotateNotes(chordNotes, StylePreset.Root), bass),
            Source = "symbol"
        };
    }

    private record ParsedToken(string Value, string Duration, bool ExplicitDuration, bool Dotted);

    public static (string Duration, bool Dotted)? DurationFromBeats(double beats, string timeSignature)
    {
        const double epsilon = 0.0001;
        var denominator = double.Parse(timeSignature.Split('/')[1], CultureInfo.InvariantCulture);

        (string Duration, bool Dotted)[] candidates =
        [
            ("1 beat", false),
            ("1 beat", true),
            ("2 beats", false),
            ("2 beats", true),
            ("1 bar", false),
            ("1 bar", true),
            ("2 bars", false)
        ];

        foreach (var candidate in candidates)
        {
            if (Math.Abs(ChordDurationBeats(candidate.Duration, timeSignature, candidate.Dotted) - beats) < epsilon)
            {
                return candidate;
            }
        }

        var eighthUnits = (beats * 8) / denominator;
        if (!double.IsInfinity(eighthUnits) && !double.IsNaN(eighthUnits) && eighthUnits > 0 && Math.Abs(eighthUnits - Math.Round(eighthUnits)) < epsilon)
        {
            return ($"{(int)Math.Round(eighthUnits)}/8", false);
        }

        return null;
    }

    public static bool IsRepeatToken(string token) => token is "%" or "=";

    public static (string Value, string Duration, bool Dotted)? ParseExplicitDurationToken(string token)
    {
        var match = ExplicitDurationTokenRegex().Match(token);
        if (!match.Success) return null;

        var val = match.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(val) || !int.TryParse(match.Groups[2].Value, out var num) || num <= 0)
        {
            return null;
        }

        return (val, $"{num}/8", match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value));
    }

    private static (List<ParsedToken> Tokens, bool Structured, List<ParseError> Errors) TokenizeInput(
        string rawInput,
        string fallbackDuration,
        string timeSignature,
        int inputSlotsPerBar)
    {
        var normalized = rawInput.Replace("\r", "");
        var beatsPerBar = double.Parse(timeSignature.Split('/')[0], CultureInfo.InvariantCulture);

        if (BarSeparatorCheckRegex().IsMatch(normalized))
        {
            var bars = BarSplitRegex().Split(normalized)
                .Select(b => b.Trim())
                .Where(b => b.Length > 0)
                .ToList();

            var structuredTokens = new List<ParsedToken>();
            var errors = new List<ParseError>();
            var pending = new List<(string Value, double Beats)>();
            string? previousValue = null;

            foreach (var bar in bars)
            {
                var slots = WhitespaceOrCommaSplitRegex().Split(bar).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (slots.Count == 0) continue;

                int[] candidateSlots = [inputSlotsPerBar, 4, 8, 12, 16];
                var effectiveSlotCount = slots.Contains("-")
                    ? candidateSlots.FirstOrDefault(c => c >= slots.Count, slots.Count)
                    : slots.Count;

                var slotBeats = beatsPerBar / effectiveSlotCount;

                foreach (var slot in slots)
                {
                    if (slot == "-")
                    {
                        if (pending.Count == 0)
                        {
                            errors.Add(new ParseError("-", "ハイフンの前にコードが必要です"));
                            continue;
                        }
                        var last = pending[^1];
                        pending[^1] = (last.Value, last.Beats + slotBeats);
                        continue;
                    }

                    if (IsRepeatToken(slot))
                    {
                        if (previousValue == null)
                        {
                            errors.Add(new ParseError(slot, "繰り返す前のコードがありません"));
                            continue;
                        }
                        pending.Add((previousValue, slotBeats));
                        continue;
                    }

                    var explicitDuration = ParseExplicitDurationToken(slot);
                    if (explicitDuration != null)
                    {
                        pending.Add((explicitDuration.Value.Value, ChordDurationBeats(explicitDuration.Value.Duration, timeSignature, explicitDuration.Value.Dotted)));
                        previousValue = explicitDuration.Value.Value;
                        continue;
                    }

                    pending.Add((slot, slotBeats));
                    previousValue = slot;
                }
            }

            foreach (var entry in pending)
            {
                var mapped = DurationFromBeats(entry.Beats, timeSignature);
                if (mapped == null)
                {
                    errors.Add(new ParseError(entry.Value, "この長さは入力表記から決定できません"));
                    continue;
                }

                structuredTokens.Add(new ParsedToken(entry.Value, mapped.Value.Duration, true, mapped.Value.Dotted));
            }

            return (structuredTokens, true, errors);
        }

        var tokens = new List<ParsedToken>();
        string? prevVal = null;
        var parts = WhitespaceOrCommaSplitRegex().Split(normalized).Select(t => t.Trim()).Where(t => t.Length > 0);

        foreach (var part in parts)
        {
            if (part == "-") continue;

            if (IsRepeatToken(part))
            {
                if (prevVal == null)
                {
                    return (tokens, false, [new ParseError(part, "繰り返す前のコードがありません")]);
                }
                tokens.Add(new ParsedToken(prevVal, fallbackDuration, false, false));
                continue;
            }

            var explicitDur = ParseExplicitDurationToken(part);
            if (explicitDur != null)
            {
                tokens.Add(new ParsedToken(explicitDur.Value.Value, explicitDur.Value.Duration, true, explicitDur.Value.Dotted));
                prevVal = explicitDur.Value.Value;
                continue;
            }

            tokens.Add(new ParsedToken(part, fallbackDuration, false, false));
            prevVal = part;
        }

        return (tokens, false, new List<ParseError>());
    }

    public static bool IsRomanToken(string token) => RomanDegree(token) != null;

    public static bool IsArabicDegreeToken(string token) =>
        ArabicDegreeRegex().IsMatch(token);

    private readonly struct PreParsedTokenInfo
    {
        public readonly int RootIndex;
        public readonly bool IsRest;
        public readonly bool IsValid;

        public PreParsedTokenInfo(int rootIndex, bool isRest, bool isValid)
        {
            RootIndex = rootIndex;
            IsRest = isRest;
            IsValid = isValid;
        }
    }

    private record CandidateScore(string Key, MusicalMode Mode, int Score);

    public static (string Key, MusicalMode Mode) DetectKeyModeFromSymbols(IReadOnlyList<string> tokens, string fallbackKey, MusicalMode fallbackMode)
    {
        var tokenCount = tokens.Count;
        var tokenInfos = new PreParsedTokenInfo[tokenCount];

        for (var i = 0; i < tokenCount; i++)
        {
            var parsed = ParseSymbolToken(tokens[i], "C", MusicalMode.Major);
            if (parsed == null)
            {
                tokenInfos[i] = new PreParsedTokenInfo(-1, false, false);
            }
            else if (parsed.Quality == ChordQuality.Rest)
            {
                tokenInfos[i] = new PreParsedTokenInfo(NoteIndex(parsed.Root), true, true);
            }
            else
            {
                tokenInfos[i] = new PreParsedTokenInfo(NoteIndex(parsed.Root), false, true);
            }
        }

        var candidates = new List<CandidateScore>(KeyCandidates.Length * 2);

        foreach (var candidateKey in KeyCandidates)
        {
            var candidateKeyIndex = NoteIndex(candidateKey);

            foreach (var candidateMode in new[] { MusicalMode.Major, MusicalMode.Minor })
            {
                var lookup = candidateMode == MusicalMode.Major ? MajorDegreeLookup : MinorDegreeLookup;
                var score = 0;

                for (var index = 0; index < tokenCount; index++)
                {
                    ref readonly var info = ref tokenInfos[index];
                    if (!info.IsValid) continue;

                    if (info.IsRest)
                    {
                        score += 3;
                        if (info.RootIndex == candidateKeyIndex) score += 1;
                        continue;
                    }

                    var distance = (info.RootIndex - candidateKeyIndex + 12) % 12;
                    var degree = lookup[distance];
                    if (degree == -1) continue;

                    score += 3;
                    if (degree < 4)
                    {
                        if (index == 0) score += 3;
                        score += 2;
                        if (index == tokenCount - 1) score += 4;
                    }
                    else
                    {
                        score += 1;
                    }

                    if (degree == 3)
                    {
                        score += 1;
                    }

                    if (distance == 0)
                    {
                        score += 1;
                    }
                }

                if (candidateKey == fallbackKey && candidateMode == fallbackMode) score += 1;
                candidates.Add(new CandidateScore(candidateKey, candidateMode, score));
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = candidates.FirstOrDefault() ?? new CandidateScore(fallbackKey, fallbackMode, -1);

        if (best.Mode == MusicalMode.Minor)
        {
            var relativeMajorKey = NoteAt(NoteIndex(best.Key) + 3, PreferFlatsForSignature(best.Key, best.Mode));
            var relativeMajor = candidates.FirstOrDefault(c => c.Key == relativeMajorKey && c.Mode == MusicalMode.Major);
            if (relativeMajor != null && best.Score - relativeMajor.Score <= 2)
            {
                return (relativeMajor.Key, relativeMajor.Mode);
            }
        }

        return (best.Key, best.Mode);
    }

    public static ParseResult ParseProgression(
        string rawInput,
        string key,
        MusicalMode mode,
        string duration,
        string timeSignature = "4/4",
        int inputSlotsPerBar = 4)
    {
        var tokenized = TokenizeInput(rawInput, duration, timeSignature, inputSlotsPerBar);
        var tokens = tokenized.Tokens;
        var looksDegreeBased = tokens.Count > 0 && tokens.All(t => IsRomanToken(t.Value) || IsArabicDegreeToken(t.Value));
        var resolved = looksDegreeBased
            ? (Key: key, Mode: mode)
            : DetectKeyModeFromSymbols(tokens.Select(t => t.Value).ToList(), key, mode);

        var chords = new List<ChordBlock>();
        var errors = new List<ParseError>(tokenized.Errors);
        var explicitDurationIndexes = new List<int>();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var arabicCandidate = ArabicDegreeToChordSymbol(token.Value, resolved.Key, resolved.Mode);
            var romanCandidate = RomanToChordSymbol(token.Value, resolved.Key, resolved.Mode);
            var symbolCandidate = ParseSymbolToken(token.Value, resolved.Key, resolved.Mode);

            var parsed = arabicCandidate ?? romanCandidate ?? symbolCandidate;
            if (parsed == null)
            {
                errors.Add(new ParseError(token.Value, "解釈できないコードです"));
                continue;
            }

            chords.Add(parsed with
            {
                Id = $"chord-{Guid.NewGuid().ToString("N")[..8]}",
                Duration = token.Duration,
                Inversion = StylePreset.Root,
                Dotted = token.Dotted
            });

            if (token.ExplicitDuration || tokenized.Structured)
            {
                explicitDurationIndexes.Add(index);
            }
        }

        return new ParseResult(chords, errors, resolved.Key, resolved.Mode, explicitDurationIndexes);
    }

    public static string ProgressionToInput(
        IReadOnlyList<ChordBlock> chords,
        NotationPreference notationPreference = NotationPreference.Symbol,
        string timeSignature = "4/4",
        int inputSlotsPerBar = 4)
    {
        var beatsPerBar = double.Parse(timeSignature.Split('/')[0], CultureInfo.InvariantCulture);
        const double epsilon = 0.0001;
        var bars = new List<List<(string Token, double Beats, bool Continued)>>();
        var currentBeat = 0.0;

        foreach (var chord in chords)
        {
            var token = (notationPreference == NotationPreference.Roman && string.IsNullOrEmpty(chord.Bass))
                ? chord.RomanNumeral
                : chord.Symbol;
            var remaining = ChordDurationBeats(chord.Duration, timeSignature, chord.Dotted);
            var continued = false;

            while (remaining > epsilon)
            {
                var barIndex = (int)Math.Floor(currentBeat / beatsPerBar);
                var beatInBar = currentBeat - barIndex * beatsPerBar;
                var chunk = Math.Min(remaining, beatsPerBar - beatInBar);

                while (bars.Count <= barIndex)
                {
                    bars.Add(new List<(string Token, double Beats, bool Continued)>());
                }

                bars[barIndex].Add((token, chunk, continued));
                currentBeat += chunk;
                remaining -= chunk;
                continued = true;
            }
        }

        var resultBars = new List<string>();
        foreach (var bar in bars)
        {
            if (bar.Count == 0) continue;
            var occupiedBeats = bar.Sum(b => b.Beats);
            var spanBeats = Math.Abs(occupiedBeats - beatsPerBar) < epsilon ? beatsPerBar : occupiedBeats;
            int[] preferredCounts = [inputSlotsPerBar, 4, 8, 6, 3, 2, 1];

            var slotCount = 1;
            foreach (var count in preferredCounts)
            {
                var slotBeats = spanBeats / count;
                if (bar.All(segment => Math.Abs(segment.Beats / slotBeats - Math.Round(segment.Beats / slotBeats)) < epsilon))
                {
                    slotCount = count;
                    break;
                }
            }

            var slotBeatsFinal = spanBeats / slotCount;
            var tokens = new List<string>();

            foreach (var segment in bar)
            {
                var units = Math.Max(1, (int)Math.Round(segment.Beats / slotBeatsFinal));
                tokens.Add(segment.Continued ? "-" : segment.Token);
                for (var i = 1; i < units; i++)
                {
                    tokens.Add("-");
                }
            }

            resultBars.Add(string.Join(" ", tokens));
        }

        return string.Join(" | ", resultBars);
    }

    public static double ChordDurationBeats(string duration, string timeSignature, bool dotted = false)
    {
        var beatsPerBar = double.Parse(timeSignature.Split('/')[0], CultureInfo.InvariantCulture);
        var denominator = double.Parse(timeSignature.Split('/')[1], CultureInfo.InvariantCulture);
        var beats = beatsPerBar;

        if (duration == "1 beat") beats = 1.0;
        else if (duration == "2 beats") beats = Math.Min(2.0, beatsPerBar);
        else if (duration == "3 beats") beats = Math.Min(3.0, beatsPerBar * 2.0);
        else if (duration == "1 bar") beats = beatsPerBar;
        else if (duration == "2 bars") beats = beatsPerBar * 2.0;
        else if (DurationEighthRegex().IsMatch(duration))
        {
            var num = double.Parse(duration.Split('/')[0], CultureInfo.InvariantCulture);
            beats = num * (denominator / 8.0);
        }
        else if (DurationSixteenthRegex().IsMatch(duration))
        {
            var num = double.Parse(duration.Split('/')[0], CultureInfo.InvariantCulture);
            beats = num * (denominator / 16.0);
        }
        else
        {
            var mBeats = BeatsDurationRegex().Match(duration);
            if (mBeats.Success && double.TryParse(mBeats.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bVal))
            {
                beats = bVal;
            }
            else
            {
                var mBars = BarsDurationRegex().Match(duration);
                if (mBars.Success && double.TryParse(mBars.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var barVal))
                {
                    beats = barVal * beatsPerBar;
                }
            }
        }

        return dotted ? beats * 1.5 : beats;
    }

    public static string BeatsToDuration(double beats, string timeSignature = "4/4")
    {
        var beatsPerBar = double.Parse(timeSignature.Split('/')[0], CultureInfo.InvariantCulture);
        const double eps = 0.001;

        if (Math.Abs(beats - beatsPerBar) < eps) return "1 bar";
        if (Math.Abs(beats - (beatsPerBar * 2.0)) < eps) return "2 bars";
        if (Math.Abs(beats - 1.0) < eps) return "1 beat";
        if (Math.Abs(beats - 2.0) < eps) return "2 beats";
        if (Math.Abs(beats - 3.0) < eps) return "3 beats";

        // 8分音符単位 (0.5拍の倍数)
        var eighths = beats * 2.0;
        if (Math.Abs(eighths - Math.Round(eighths)) < eps)
        {
            return $"{(int)Math.Round(eighths)}/8";
        }

        // 16分音符単位 (0.25拍の倍数)
        var sixteenths = beats * 4.0;
        if (Math.Abs(sixteenths - Math.Round(sixteenths)) < eps)
        {
            return $"{(int)Math.Round(sixteenths)}/16";
        }

        return $"{beats.ToString("0.##", CultureInfo.InvariantCulture)} beats";
    }

    public static string DurationLabel(string duration, bool dotted = false)
    {
        bool isEn = LocalizationService.IsEnglish;
        string baseLabel;
        if (duration == "1 beat") baseLabel = isEn ? "1 Beat" : "1拍";
        else if (duration == "2 beats") baseLabel = isEn ? "2 Beats" : "2拍";
        else if (duration == "3 beats") baseLabel = isEn ? "3 Beats" : "3拍";
        else if (duration == "1 bar") baseLabel = isEn ? "1 Bar" : "1小節";
        else if (duration == "2 bars") baseLabel = isEn ? "2 Bars" : "2小節";
        else if (DurationEighthRegex().IsMatch(duration))
        {
            var num = int.Parse(duration.Split('/')[0], CultureInfo.InvariantCulture);
            baseLabel = num switch
            {
                1 => isEn ? "0.5 Beats" : "0.5拍",
                2 => isEn ? "1 Beat" : "1拍",
                3 => isEn ? "1.5 Beats" : "1.5拍",
                4 => isEn ? "2 Beats" : "2拍",
                5 => isEn ? "2.5 Beats" : "2.5拍",
                6 => isEn ? "3 Beats" : "3拍",
                7 => isEn ? "3.5 Beats" : "3.5拍",
                8 => isEn ? "1 Bar" : "1小節",
                _ => isEn ? $"{num * 0.5:0.#} Beats" : $"{num * 0.5:0.#}拍"
            };
        }
        else if (DurationSixteenthRegex().IsMatch(duration))
        {
            var num = int.Parse(duration.Split('/')[0], CultureInfo.InvariantCulture);
            baseLabel = isEn ? $"{num * 0.25:0.##} Beats" : $"{num * 0.25:0.##}拍";
        }
        else
        {
            var m = BeatsDurationRegex().Match(duration);
            if (m.Success)
            {
                baseLabel = isEn ? $"{m.Groups[1].Value} Beats" : $"{m.Groups[1].Value}拍";
            }
            else
            {
                baseLabel = duration;
            }
        }
        return $"{baseLabel}{(dotted ? "・" : "")}";
    }

    public static string StyleLabel(StylePreset style)
    {
        bool isEn = LocalizationService.IsEnglish;
        return style switch
        {
            StylePreset.First => isEn ? "1st Inv" : "1転",
            StylePreset.Second => isEn ? "2nd Inv" : "2転",
            StylePreset.Third => isEn ? "3rd Inv" : "3転",
            _ => isEn ? "Root" : "基本"
        };
    }

    public static List<int> BarLineIndexes(IReadOnlyList<ChordBlock> chords, string timeSignature)
    {
        var beatsPerBar = double.Parse(timeSignature.Split('/')[0], CultureInfo.InvariantCulture);
        const double epsilon = 0.0001;
        var accumulated = 0.0;
        var indexes = new List<int>();

        for (var index = 0; index < chords.Count; index++)
        {
            accumulated += ChordDurationBeats(chords[index].Duration, timeSignature, chords[index].Dotted);
            var remainder = accumulated % beatsPerBar;
            if (Math.Abs(remainder) < epsilon || Math.Abs(remainder - beatsPerBar) < epsilon)
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    public static List<int> MidiNoteNumbers(
        ChordBlock chord,
        BassAdditionMode bassAddition = BassAdditionMode.None,
        OpenVoicingMode openVoicing = OpenVoicingMode.Closed)
    {
        if (chord.Quality == ChordQuality.Rest) return [];

        const int rootBase = 60;
        var rootOffset = NoteIndex(chord.Root);
        var intervals = chord.Intervals ?? QualityIntervals[chord.Quality];
        var inversion = !string.IsNullOrEmpty(chord.Bass) ? StylePreset.Root : chord.Inversion;
        var inversionSteps = inversion == StylePreset.First ? 1 : inversion == StylePreset.Second ? 2 : inversion == StylePreset.Third ? 3 : 0;
        var safeSteps = Math.Min(inversionSteps, Math.Max(0, intervals.Count - 1));

        var chordNotes = intervals.Select(i => rootBase + rootOffset + i).ToList();
        for (var i = 0; i < safeSteps; i++)
        {
            chordNotes[i] += 12;
        }

        if (openVoicing == OpenVoicingMode.Third && chordNotes.Count >= 2)
        {
            chordNotes[1] += 12;
        }
        else if (openVoicing == OpenVoicingMode.Fifth && chordNotes.Count >= 3)
        {
            chordNotes[2] += 12;
        }

        chordNotes.Sort();

        var bassSource = chord.Bass ?? chord.Root;
        var bassOffset = NoteIndex(bassSource);
        if (bassOffset < 0) return chordNotes;

        var bassNotes = new List<int>();
        if (!string.IsNullOrEmpty(chord.Bass))
        {
            bassNotes.Add(48 + bassOffset);
        }
        if (bassAddition is BassAdditionMode.One or BassAdditionMode.Both)
        {
            bassNotes.Add(24 + bassOffset);
        }
        if (bassAddition is BassAdditionMode.Two or BassAdditionMode.Both)
        {
            bassNotes.Add(36 + bassOffset);
        }

        return bassNotes.Concat(chordNotes).Distinct().OrderBy(n => n).ToList();
    }

    public static List<SuggestionItem> DiatonicChords(string key, MusicalMode mode)
    {
        var keyIndex = NoteIndex(key);
        var degrees = mode == MusicalMode.Major ? DegreeOffsetsMajor : DegreeOffsetsMinor;
        ChordQuality[] qualities = mode == MusicalMode.Major
            ? [ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major, ChordQuality.Minor, ChordQuality.HalfDiminished]
            : [ChordQuality.Minor, ChordQuality.Diminished, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major];
        var labels = mode == MusicalMode.Major ? DegreeLabelsMajor : DegreeLabelsMinor;
        var preferFlats = PreferFlatsForSignature(key, mode);

        return degrees.Select((dist, idx) => new SuggestionItem(
            ChordSymbol(NoteAt(keyIndex + dist, preferFlats), qualities[idx]),
            labels[idx]
        )).ToList();
    }

    public static List<SuggestionItem> ModalInterchangeChords(string key, MusicalMode mode)
    {
        var keyIndex = NoteIndex(key);
        (int Dist, ChordQuality Quality, string Roman)[] borrowed =
        [
            (5, ChordQuality.Minor, "iv"),
            (8, ChordQuality.Major, "bVI"),
            (10, ChordQuality.Major, "bVII"),
            (3, ChordQuality.Major, "bIII"),
            (7, ChordQuality.Minor, "v"),
            (2, ChordQuality.HalfDiminished, "iiø7"),
            (1, ChordQuality.Major, "bII")
        ];

        return borrowed.Select(b => new SuggestionItem(
            ChordSymbol(NoteAt(keyIndex + b.Dist, true), b.Quality),
            b.Roman
        )).ToList();
    }

    public static List<SuggestionItem> SecondaryDominantChords(string key, MusicalMode mode)
    {
        var keyIndex = NoteIndex(key);
        var preferFlats = PreferFlatsForSignature(key, mode);
        (int Dist, ChordQuality Quality, string Roman)[] dominants =
        [
            (9, ChordQuality.Dominant7, "V7/ii"),
            (11, ChordQuality.Dominant7, "V7/iii"),
            (0, ChordQuality.Dominant7, "V7/IV"),
            (2, ChordQuality.Dominant7, "V7/V"),
            (4, ChordQuality.Dominant7, "V7/vi")
        ];

        return dominants.Select(d => new SuggestionItem(
            ChordSymbol(NoteAt(keyIndex + d.Dist, preferFlats), d.Quality),
            d.Roman
        )).ToList();
    }

    public static List<SuggestionItem> SuggestionSet(IReadOnlyList<ChordBlock> chords, string key, MusicalMode mode)
    {
        var @base = DiatonicChords(key, mode);
        List<SuggestionItem> Pick(int[] indexes) => indexes.Where(i => i >= 0 && i < @base.Count).Select(i => @base[i]).ToList();

        if (chords.Count == 0) return Pick([0, 4, 5, 3]);
        var last = chords[^1];
        if (last.RomanNumeral.StartsWith("V", StringComparison.OrdinalIgnoreCase)) return Pick([0, 5, 3, 1]);
        if (last.RomanNumeral.StartsWith("vi", StringComparison.OrdinalIgnoreCase)) return Pick([3, 4, 0, 1]);
        if (last.RomanNumeral.StartsWith("ii", StringComparison.OrdinalIgnoreCase)) return Pick([4, 0, 5, 3]);
        return Pick([4, 5, 3, 2]);
    }

    public static ChordBlock TransposeChord(ChordBlock chord, int semitones, string key, MusicalMode mode)
    {
        if (chord.Quality == ChordQuality.Rest) return chord;
        var preferFlats = PreferFlatsForSignature(key, mode) || chord.Root.Contains('b');
        var transposedRoot = NoteAt(NoteIndex(chord.Root) + semitones, preferFlats);
        var quality = chord.Quality;
        var bass = !string.IsNullOrEmpty(chord.Bass) ? NoteAt(NoteIndex(chord.Bass) + semitones, preferFlats) : null;

        var chordNotes = chord.Intervals != null
            ? BuildNotesFromIntervals(transposedRoot, chord.Intervals)
            : BuildChordNotes(transposedRoot, quality);

        return chord with
        {
            Root = transposedRoot,
            Bass = bass,
            Symbol = ChordSymbol(transposedRoot, quality, chord.Descriptor, bass),
            RomanNumeral = ChordToRoman(transposedRoot, quality, key, mode),
            Notes = WithBass(chordNotes, bass)
        };
    }

    public static List<ChordBlock> TransposeProgression(IReadOnlyList<ChordBlock> chords, int semitones, string key, MusicalMode mode)
    {
        return chords.Select(c => TransposeChord(c, semitones, key, mode)).ToList();
    }

    public static ChordBlock ApplyInversion(ChordBlock chord, StylePreset inversion)
    {
        if (chord.Quality == ChordQuality.Rest) return chord;

        if (!string.IsNullOrEmpty(chord.Bass))
        {
            return chord with
            {
                Inversion = StylePreset.Root,
                Notes = WithBass(RotateNotes(BuildChordNotes(chord.Root, chord.Quality), StylePreset.Root), chord.Bass)
            };
        }

        var chordNotes = chord.Intervals != null
            ? BuildNotesFromIntervals(chord.Root, chord.Intervals)
            : BuildChordNotes(chord.Root, chord.Quality);

        return chord with
        {
            Inversion = inversion,
            Notes = RotateNotes(chordNotes, inversion)
        };
    }
}
