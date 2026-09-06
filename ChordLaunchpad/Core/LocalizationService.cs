using System;
using System.Collections.Generic;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Core;

public static class LocalizationService
{
    public const string LanguageJapanese = "ja-JP";
    public const string LanguageEnglish = "en-US";

    private static string _currentLanguage = LanguageJapanese;

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value == LanguageEnglish ? LanguageEnglish : LanguageJapanese;
                LanguageChanged?.Invoke();
            }
        }
    }

    public static bool IsEnglish => _currentLanguage == LanguageEnglish;

    public static event Action? LanguageChanged;

    static LocalizationService()
    {
        // 初期言語を設定から読み込む
        var settings = SettingsManager.Current;
        _currentLanguage = settings.AppLanguage == LanguageEnglish ? LanguageEnglish : LanguageJapanese;
    }

    public static string Get(string key)
    {
        if (Dictionaries.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var text))
        {
            return text;
        }

        // フォールバック: 日本語辞書
        if (Dictionaries[LanguageJapanese].TryGetValue(key, out var jaText))
        {
            return jaText;
        }

        return key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Dictionaries = new()
    {
        [LanguageJapanese] = new()
        {
            ["Menu_File"] = "ファイル",
            ["Menu_File_Save"] = "プロジェクトを保存",
            ["Menu_File_New"] = "新規プロジェクト",
            ["Menu_File_Open"] = "プロジェクトを開く...",
            ["Menu_File_SaveAs"] = "名前を付けて保存...",
            ["Menu_File_ExportMidi"] = "MIDIとしてエクスポート...",
            ["Menu_File_Exit"] = "終了"
        },
        [LanguageEnglish] = new()
        {
            ["Menu_File"] = "File",
            ["Menu_File_Save"] = "Save Project",
            ["Menu_File_New"] = "New Project",
            ["Menu_File_Open"] = "Open Project...",
            ["Menu_File_SaveAs"] = "Save As...",
            ["Menu_File_ExportMidi"] = "Export MIDI...",
            ["Menu_File_Exit"] = "Exit"
        }
    };
}
