using System;
using System.IO;
using System.Text.Json;
using Windows.System;

namespace ChordLaunchpad.Core.Models;

public enum ZoomShortcutStyle
{
    ProTools,          // Zoom Out: R, Zoom In: T (修飾キーなし)
    Cubase,            // Zoom Out: G, Zoom In: H (修飾キーなし)
    StudioOne,         // Zoom Out: W, Zoom In: E (修飾キーなし)
    PremiereResolve,   // Zoom Out: Ctrl + - / =, Zoom In: Ctrl + + / ; / = (修飾キーCtrl必須)
    AvidMediaComposer, // Zoom Out: Ctrl + K, Zoom In: Ctrl + L (修飾キーCtrl必須)
    Custom             // ユーザー定義キー
}

public class AppSettings
{
    public ZoomShortcutStyle ZoomStyle { get; set; } = ZoomShortcutStyle.ProTools;

    // テンキー (+/-) によるズームの単独ON/OFF切り替え
    public bool EnableNumpadZoom { get; set; } = true;

    // カスタム設定時のキー設定
    public VirtualKey CustomZoomInKey { get; set; } = VirtualKey.T;
    public bool CustomZoomInCtrl { get; set; } = false;
    public bool CustomZoomInAlt { get; set; } = false;
    public bool CustomZoomInShift { get; set; } = false;

    public VirtualKey CustomZoomOutKey { get; set; } = VirtualKey.R;
    public bool CustomZoomOutCtrl { get; set; } = false;
    public bool CustomZoomOutAlt { get; set; } = false;
    public bool CustomZoomOutShift { get; set; } = false;

    // ホイールズームの有効/無効 (Ctrl + ホイール)
    public bool EnableWheelZoom { get; set; } = true;

    // UI表示言語 ("ja-JP" or "en-US")
    public string AppLanguage { get; set; } = "ja-JP";

    // プロジェクト一時ファイルの自動保存設定
    public bool EnableAutoSave { get; set; } = true;
    public int AutoSaveIntervalMinutes { get; set; } = 5; // 分単位
    public int AutoSaveMaxBackups { get; set; } = 10;      // 保持する一時ファイル数

    // プロジェクト保存先ディレクトリ設定
    // true: 事前に設定した固定ディレクトリを使用 / false: 毎回ユーザーに選択させる (既定)
    public bool UseDefaultProjectDirectory { get; set; } = false;
    public string DefaultProjectDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ChordLaunchpad Projects");
}

public static class SettingsManager
{
    private static readonly string SettingsFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChordLaunchpad");

    private static readonly string SettingsFilePath = Path.Combine(SettingsFolderPath, "settings.json");

    private static AppSettings? _cachedSettings;

    public static AppSettings Current
    {
        get
        {
            if (_cachedSettings == null)
            {
                _cachedSettings = LoadSettings();
            }
            return _cachedSettings;
        }
        set => _cachedSettings = value;
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // エラー時はデフォルト設定を安全に返す
        }

        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            _cachedSettings = settings;
            if (!Directory.Exists(SettingsFolderPath))
            {
                Directory.CreateDirectory(SettingsFolderPath);
            }

            var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // 保存エラー時もアプリクラッシュを防ぐ
        }
    }
}
