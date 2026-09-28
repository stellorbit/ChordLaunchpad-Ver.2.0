using System;
using System.Collections.Generic;
using System.Linq;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Core;

/// <summary>
/// 外部プロジェクトファイル（.chord / .json）読み込み時のデータ境界値検証およびサニタイズを行うバリデーター
/// </summary>
public static class ProjectDataValidator
{
    public const int MaxChordsCount = 300;
    public const int MaxMarkersCount = 50;
    public const int MaxRawInputLength = 4096;
    public const int MaxTitleLength = 128;
    public const int MaxSymbolLength = 64;
    public const int MinBpm = 20;
    public const int MaxBpm = 400;
    public const int DefaultBpm = 120;

    public static readonly string[] ValidKeys =
    {
        "C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B"
    };

    /// <summary>
    /// 外部から逆シリアル化された ProjectData を検証し、安全な範囲内にクランプ・サニタイズしたインスタンスを返す
    /// </summary>
    public static ProjectData ValidateAndSanitize(ProjectData? raw)
    {
        if (raw == null)
        {
            return new ProjectData();
        }

        // BPM の境界値チェック
        int sanitizedBpm = raw.Bpm;
        if (sanitizedBpm < MinBpm || sanitizedBpm > MaxBpm)
        {
            sanitizedBpm = Math.Clamp(sanitizedBpm, MinBpm, MaxBpm);
        }

        // Key の検証（有効なキーの場合は正規化された表記に変換、不正な場合は "C" にフォールバック）
        string rawKey = raw.Key?.Trim() ?? "C";
        var matchedKey = ValidKeys.FirstOrDefault(k => string.Equals(k, rawKey, StringComparison.OrdinalIgnoreCase));
        string sanitizedKey = matchedKey ?? "C";

        // Title の検証
        string sanitizedTitle = string.IsNullOrWhiteSpace(raw.Title)
            ? "Untitled Progression"
            : raw.Title.Trim();
        if (sanitizedTitle.Length > MaxTitleLength)
        {
            sanitizedTitle = sanitizedTitle[..MaxTitleLength];
        }

        // RawInput の検証
        string sanitizedRawInput = raw.RawInput ?? string.Empty;
        if (sanitizedRawInput.Length > MaxRawInputLength)
        {
            sanitizedRawInput = sanitizedRawInput[..MaxRawInputLength];
        }

        // Chords の検証・上限切り詰め
        var rawChords = raw.Chords ?? Array.Empty<ChordBlock>();
        var sanitizedChords = rawChords
            .Take(MaxChordsCount)
            .Select(c =>
            {
                var symbol = c.Symbol ?? string.Empty;
                if (symbol.Length > MaxSymbolLength) symbol = symbol[..MaxSymbolLength];
                var root = string.IsNullOrWhiteSpace(c.Root) ? "C" : c.Root.Trim();
                var duration = string.IsNullOrWhiteSpace(c.Duration) ? "1 bar" : c.Duration.Trim();
                if (duration.Length > 32) duration = "1 bar";
                var id = string.IsNullOrWhiteSpace(c.Id) ? $"chord-{Guid.NewGuid():N}" : c.Id;

                return c with
                {
                    Id = id,
                    Symbol = symbol,
                    Root = root,
                    Duration = duration
                };
            })
            .ToList();

        // Markers の検証（InsertIndexのクランプ、Nameの長さ制限）
        var rawMarkers = raw.Markers ?? Array.Empty<SectionMarker>();
        int chordCount = sanitizedChords.Count;
        var sanitizedMarkers = rawMarkers
            .Take(MaxMarkersCount)
            .Select(m =>
            {
                var name = string.IsNullOrWhiteSpace(m.Name) ? "Section" : m.Name.Trim();
                if (name.Length > 64) name = name[..64];
                var key = string.IsNullOrWhiteSpace(m.Key) ? sanitizedKey : m.Key.Trim();
                if (!ValidKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) key = sanitizedKey;
                var insertIndex = Math.Clamp(m.InsertIndex, 0, chordCount);
                var id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N")[..8] : m.Id;

                return m with
                {
                    Id = id,
                    Name = name,
                    Key = key,
                    InsertIndex = insertIndex
                };
            })
            .ToList();

        return raw with
        {
            Title = sanitizedTitle,
            Key = sanitizedKey,
            Bpm = sanitizedBpm,
            RawInput = sanitizedRawInput,
            Chords = sanitizedChords,
            Markers = sanitizedMarkers
        };
    }
}
