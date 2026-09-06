using System;
using System.Collections.Generic;

namespace ChordLaunchpad.Core.Models;

public enum MusicalMode
{
    Major,
    Minor
}

public enum TimeSignatureType
{
    FourFour,
    ThreeFour
}

public enum StylePreset
{
    Root,
    First,
    Second,
    Third
}

public enum BassAdditionMode
{
    None,
    One,
    Two,
    Both
}

public enum OpenVoicingMode
{
    Closed,
    Third,
    Fifth
}

public enum NotationPreference
{
    Roman,
    Symbol
}

public enum ChordQuality
{
    Rest,
    Major,
    Minor,
    Diminished,
    HalfDiminished,
    Augmented,
    Dominant7,
    Major7,
    Minor7,
    Add9,
    MinorAdd9,
    Sus2,
    Sixth,
    Minor6,
    Sus4
}

public record ChordBlock
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; init; } = string.Empty;
    public string Root { get; init; } = "C";
    public string? Bass { get; init; }
    public StylePreset Inversion { get; init; } = StylePreset.Root;
    public bool BarAfter { get; init; }
    public ChordQuality Quality { get; init; } = ChordQuality.Major;
    public string? Descriptor { get; init; }
    public IReadOnlyList<int>? Intervals { get; init; }
    public string RomanNumeral { get; init; } = "I";
    public string Duration { get; init; } = "1 bar";
    public bool Dotted { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public string Source { get; init; } = "symbol";
    public bool IsSelected { get; init; }
    public double CardWidth { get; init; } = 140.0;
}

public record ParseError(string Token, string Message);

public record ParseResult(
    IReadOnlyList<ChordBlock> Chords,
    IReadOnlyList<ParseError> Errors,
    string ResolvedKey,
    MusicalMode ResolvedMode,
    IReadOnlyList<int> ExplicitDurationIndexes
);

public record ProgressionTemplate(
    string Id,
    string Category,
    string Name,
    string Progression,
    string Description,
    string MajorCategory = ""
);

public record SuggestionItem(
    string Symbol,
    string RomanNumeral
);

public record SectionMarker
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Section";
    public string Key { get; set; } = "C";
    public MusicalMode Mode { get; set; } = MusicalMode.Major;
    public int InsertIndex { get; set; } = 0;
}

public record TimelineHistoryState(
    IReadOnlyList<ChordBlock> Chords,
    IReadOnlyList<SectionMarker> Markers,
    string? SelectedChordId,
    string Key,
    MusicalMode Mode
);

public record ProjectData
{
    public string Title { get; init; } = "Untitled Progression";
    public string Key { get; init; } = "C";
    public MusicalMode Mode { get; init; } = MusicalMode.Major;
    public int Bpm { get; init; } = 120;
    public string TimeSignature { get; init; } = "4/4";
    public int InputSlotsPerBar { get; init; } = 4;
    public Audio.PlaybackTone PlaybackTone { get; init; } = Audio.PlaybackTone.Piano;
    public BassAdditionMode BassAddition { get; init; } = BassAdditionMode.None;
    public OpenVoicingMode OpenVoicing { get; init; } = OpenVoicingMode.Closed;
    public string ChordDuration { get; init; } = "1 bar";
    public StylePreset Style { get; init; } = StylePreset.Root;
    public NotationPreference NotationPreference { get; init; } = NotationPreference.Roman;
    public string RawInput { get; init; } = "I V vi IV";
    public IReadOnlyList<ChordBlock> Chords { get; init; } = Array.Empty<ChordBlock>();
    public IReadOnlyList<SectionMarker> Markers { get; init; } = Array.Empty<SectionMarker>();
}

public class ChordCardItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private ChordBlock _model;
    private string _durationDisplay = string.Empty;

    public ChordBlock Model
    {
        get => _model;
        set
        {
            if (SetProperty(ref _model, value))
            {
                _durationDisplay = MusicEngine.DurationLabel(value.Duration, value.Dotted);
                OnPropertyChanged(nameof(Id));
                OnPropertyChanged(nameof(Symbol));
                OnPropertyChanged(nameof(RomanNumeral));
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(Dotted));
                OnPropertyChanged(nameof(DurationDisplay));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private double _cardWidth;
    public double CardWidth
    {
        get => _cardWidth;
        set => SetProperty(ref _cardWidth, value);
    }

    public string Id => _model.Id;
    public string Symbol => _model.Symbol;
    public string RomanNumeral => _model.RomanNumeral;
    public string Duration => _model.Duration;
    public bool Dotted => _model.Dotted;
    public string DurationDisplay => _durationDisplay;

    public ChordCardItem(ChordBlock model, bool isSelected = false, double cardWidth = 140.0)
    {
        _model = model;
        _durationDisplay = MusicEngine.DurationLabel(model.Duration, model.Dotted);
        _isSelected = isSelected;
        _cardWidth = cardWidth;
    }
}
