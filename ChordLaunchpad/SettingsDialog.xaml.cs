using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly List<VirtualKey> _candidateKeys = new()
    {
        VirtualKey.A, VirtualKey.B, VirtualKey.C, VirtualKey.D, VirtualKey.E,
        VirtualKey.F, VirtualKey.G, VirtualKey.H, VirtualKey.I, VirtualKey.J,
        VirtualKey.K, VirtualKey.L, VirtualKey.M, VirtualKey.N, VirtualKey.O,
        VirtualKey.P, VirtualKey.Q, VirtualKey.R, VirtualKey.S, VirtualKey.T,
        VirtualKey.U, VirtualKey.V, VirtualKey.W, VirtualKey.X, VirtualKey.Y,
        VirtualKey.Z,
        VirtualKey.Number0, VirtualKey.Number1, VirtualKey.Number2, VirtualKey.Number3,
        VirtualKey.Number4, VirtualKey.Number5, VirtualKey.Number6, VirtualKey.Number7,
        VirtualKey.Number8, VirtualKey.Number9,
        VirtualKey.Add, VirtualKey.Subtract,
        VirtualKey.Up, VirtualKey.Down, VirtualKey.Left, VirtualKey.Right,
        VirtualKey.PageUp, VirtualKey.PageDown
    };

    public SettingsDialog()
    {
        InitializeComponent();
        PopulateKeyCandidates();
        LoadCurrentSettings();
    }

    private void PopulateKeyCandidates()
    {
        CustomInKeyComboBox.Items.Clear();
        CustomOutKeyComboBox.Items.Clear();

        foreach (var key in _candidateKeys)
        {
            CustomInKeyComboBox.Items.Add(key.ToString());
            CustomOutKeyComboBox.Items.Add(key.ToString());
        }
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.Current;

        // 言語の選択
        LanguageComboBox.SelectedIndex = settings.AppLanguage == ChordLaunchpad.Core.LocalizationService.LanguageEnglish ? 1 : 0;

        // ズームスタイルの選択
        switch (settings.ZoomStyle)
        {
            case ZoomShortcutStyle.ProTools:
                ZoomStyleComboBox.SelectedIndex = 0;
                break;
            case ZoomShortcutStyle.Cubase:
                ZoomStyleComboBox.SelectedIndex = 1;
                break;
            case ZoomShortcutStyle.StudioOne:
                ZoomStyleComboBox.SelectedIndex = 2;
                break;
            case ZoomShortcutStyle.PremiereResolve:
                ZoomStyleComboBox.SelectedIndex = 3;
                break;
            case ZoomShortcutStyle.AvidMediaComposer:
                ZoomStyleComboBox.SelectedIndex = 4;
                break;
            case ZoomShortcutStyle.Custom:
                ZoomStyleComboBox.SelectedIndex = 5;
                break;
            default:
                ZoomStyleComboBox.SelectedIndex = 0;
                break;
        }

        // カスタムキー
        CustomInKeyComboBox.SelectedItem = settings.CustomZoomInKey.ToString();
        CustomInCtrlCheck.IsChecked = settings.CustomZoomInCtrl;
        CustomInAltCheck.IsChecked = settings.CustomZoomInAlt;
        CustomInShiftCheck.IsChecked = settings.CustomZoomInShift;

        CustomOutKeyComboBox.SelectedItem = settings.CustomZoomOutKey.ToString();
        CustomOutCtrlCheck.IsChecked = settings.CustomZoomOutCtrl;
        CustomOutAltCheck.IsChecked = settings.CustomZoomOutAlt;
        CustomOutShiftCheck.IsChecked = settings.CustomZoomOutShift;

        // テンキーズーム & ホイールズーム
        EnableNumpadZoomToggle.IsOn = settings.EnableNumpadZoom;
        EnableWheelZoomToggle.IsOn = settings.EnableWheelZoom;

        // プロジェクト保存先設定
        AskEachTimeRadio.IsChecked = !settings.UseDefaultProjectDirectory;
        UseDefaultDirRadio.IsChecked = settings.UseDefaultProjectDirectory;
        DefaultDirPanel.Visibility = settings.UseDefaultProjectDirectory ? Visibility.Visible : Visibility.Collapsed;
        DefaultDirTextBox.Text = string.IsNullOrEmpty(settings.DefaultProjectDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordLaunchpad Projects")
            : settings.DefaultProjectDirectory;

        // 自動保存バックアップ
        EnableAutoSaveToggle.IsOn = settings.EnableAutoSave;
        AutoSaveIntervalBox.Value = Math.Clamp(settings.AutoSaveIntervalMinutes, 1, 120);
        AutoSaveMaxBackupsBox.Value = Math.Clamp(settings.AutoSaveMaxBackups, 1, 99);

        UpdateDialogLanguage(settings.AppLanguage);
        UpdateDescription();
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            UpdateDialogLanguage(lang);
            UpdateDescription();
        }
    }

    private void UpdateDialogLanguage(string lang)
    {
        bool isEn = lang == ChordLaunchpad.Core.LocalizationService.LanguageEnglish;

        Title = isEn ? "Settings" : "環境設定 (Settings)";
        PrimaryButtonText = isEn ? "Save & Apply" : "保存して適用";
        CloseButtonText = isEn ? "Cancel" : "キャンセル";

        SettingsDescriptionText.Text = isEn
            ? "Configure timeline zoom keybindings, project storage locations, and auto-backup settings."
            : "タイムラインの横ズーム操作スタイルやプロジェクト保存先・自動バックアップ環境を設定します。";

        LanguageCardTitleText.Text = isEn ? "Display Language" : "表示言語 / Language";
        LanguageCardDescText.Text = isEn ? "Select the user interface language." : "ユーザーインターフェースの表示言語を選択します。";

        ZoomStyleTitleText.Text = isEn ? "Timeline Zoom Shortcut Style" : "タイムライン横ズーム操作スタイル";
        ZoomStyleSubText.Text = isEn ? "Choose keybindings matching your preferred DAW or video editing software." : "お使いのDAWや映像編集ソフトに合わせたキーバインドを選択できます。";

        CustomKeyTitleText.Text = isEn ? "Custom Zoom Key Bindings" : "カスタム・ズームキー割り当て";
        CustomInLabel.Text = isEn ? "Zoom In:" : "ズーム拡大 (In):";
        CustomOutLabel.Text = isEn ? "Zoom Out:" : "ズーム縮小 (Out):";

        NumpadZoomTitleText.Text = isEn ? "Numeric Keypad [+] / [-] Zoom" : "テンキーの [+] / [-] によるズーム操作";
        NumpadZoomDescText.Text = isEn ? "Press Numpad + or - directly to zoom the timeline without modifiers." : "テンキー側のプラス・マイナスキーを単独で押してタイムラインを拡縮します。";
        EnableNumpadZoomToggle.OnContent = isEn ? "Enabled" : "有効";
        EnableNumpadZoomToggle.OffContent = isEn ? "Disabled" : "無効";

        WheelZoomTitleText.Text = isEn ? "Mouse Wheel Zoom Integration (Ctrl + Wheel)" : "マウスホイールによるズーム連携 (Ctrl + ホイール)";
        WheelZoomDescText.Text = isEn ? "Hold [Ctrl] and scroll the mouse wheel over the timeline for smooth zoom." : "タイムライン上で [Ctrl] キーを押しながらホイール回転でスムーズ拡縮します。";
        EnableWheelZoomToggle.OnContent = isEn ? "Enabled" : "有効";
        EnableWheelZoomToggle.OffContent = isEn ? "Disabled" : "無効";

        ProjectDirTitleText.Text = isEn ? "Project Folder Management" : "プロジェクト保存先フォルダの扱い";
        AskEachTimeRadio.Content = isEn ? "Ask for folder location on every new project (Recommended)" : "毎回保存先ディレクトリを選択する (推奨)";
        UseDefaultDirRadio.Content = isEn ? "Automatically store projects in a pre-configured directory" : "指定の固定ディレクトリに自動作成・格納する";
        BrowseDirButton.Content = isEn ? "Browse..." : "参照...";

        AutoSaveTitleText.Text = isEn ? "Automatic Project Backup (Project Backup)" : "プロジェクト一時ファイルの自動保存 (Project Backup)";
        AutoSaveDescText.Text = isEn ? "Automatically creates numbered backups inside 'Project Backup' folder." : "プロジェクトフォルダ内の「Project Backup」に連番ファイルで自動退避します。";
        EnableAutoSaveToggle.OnContent = isEn ? "Enabled" : "有効";
        EnableAutoSaveToggle.OffContent = isEn ? "Disabled" : "無効";
        AutoSaveIntervalLabel.Text = isEn ? "Interval (min):" : "保存間隔 (分):";
        AutoSaveMaxBackupsLabel.Text = isEn ? "Max Backups:" : "保持世代数 (回数):";

        foreach (var itemObj in ZoomStyleComboBox.Items)
        {
            if (itemObj is ComboBoxItem comboItem && comboItem.Tag is string tag)
            {
                comboItem.Content = (tag, isEn) switch
                {
                    ("ProTools", true) => "Pro Tools style (R/T)",
                    ("ProTools", false) => "Pro Tools スタイル (R/T)",
                    ("Cubase", true) => "Cubase style (G/H)",
                    ("Cubase", false) => "Cubase スタイル (G/H)",
                    ("StudioOne", true) => "Studio One style (W/E)",
                    ("StudioOne", false) => "Studio One スタイル (W/E)",
                    ("PremiereResolve", true) => "Premiere / Resolve / FCP (Ctrl + -/=)",
                    ("PremiereResolve", false) => "Premiere / Resolve / FCP (Ctrl + -/=)",
                    ("AvidMediaComposer", true) => "Avid Media Composer (Ctrl + K/L)",
                    ("AvidMediaComposer", false) => "Avid Media Composer (Ctrl + K/L)",
                    ("Custom", true) => "Custom...",
                    ("Custom", false) => "カスタム設定...",
                    _ => comboItem.Content
                };
            }
        }
    }

    private void ZoomStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            CustomKeyCard.Visibility = (tag == "Custom") ? Visibility.Visible : Visibility.Collapsed;
            UpdateDescription();
        }
    }

    private void ProjectLocationRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DefaultDirPanel.Visibility = UseDefaultDirRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseDefaultDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            if (App.MainWindowInstance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                DefaultDirTextBox.Text = folder.Path;
            }
        }
        catch
        {
            // フォルダー選択キャンセル・失敗時は何もしない
        }
    }

    private void UpdateDescription()
    {
        bool isEn = LanguageComboBox.SelectedItem is ComboBoxItem langItem &&
                    langItem.Tag as string == ChordLaunchpad.Core.LocalizationService.LanguageEnglish;

        if (ZoomStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (isEn)
            {
                ZoomStyleDescriptionText.Text = tag switch
                {
                    "ProTools" => "Pro Tools style: [R] Zoom Out, [T] Zoom In (Direct key)",
                    "Cubase" => "Cubase style: [G] Zoom Out, [H] Zoom In (Direct key)",
                    "StudioOne" => "Studio One style: [W] Zoom Out, [E] Zoom In (Direct key)",
                    "PremiereResolve" => "Premiere / Resolve / FCP style: [Ctrl + -] Zoom Out, [Ctrl + +] Zoom In (Ctrl modifier)",
                    "AvidMediaComposer" => "Avid Media Composer style: [Ctrl + K] Zoom Out, [Ctrl + L] Zoom In (Ctrl modifier)",
                    "Custom" => "Using custom user-assigned shortcut keys",
                    _ => ""
                };
            }
            else
            {
                ZoomStyleDescriptionText.Text = tag switch
                {
                    "ProTools" => "Pro Tools仕様: [R]キーで縮小、[T]キーで拡大 (単独キー)",
                    "Cubase" => "Cubase仕様: [G]キーで縮小、[H]キーで拡大 (単独キー)",
                    "StudioOne" => "Studio One仕様: [W]キーで縮小、[E]キーで拡大 (単独キー)",
                    "PremiereResolve" => "Premiere / Resolve / Final Cut仕様: [Ctrl + -]で縮小、[Ctrl + +]で拡大 (要Ctrl修飾)",
                    "AvidMediaComposer" => "Avid Media Composer仕様: [Ctrl + K]で縮小、[Ctrl + L]で拡大 (要Ctrl修飾)",
                    "Custom" => "ユーザー定義のカスタムキー割り当てを使用します",
                    _ => ""
                };
            }
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var settings = SettingsManager.Current;

        // 言語設定
        if (LanguageComboBox.SelectedItem is ComboBoxItem langItem && langItem.Tag is string langTag)
        {
            settings.AppLanguage = langTag;
            ChordLaunchpad.Core.LocalizationService.CurrentLanguage = langTag;
        }

        if (ZoomStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            settings.ZoomStyle = tag switch
            {
                "ProTools" => ZoomShortcutStyle.ProTools,
                "Cubase" => ZoomShortcutStyle.Cubase,
                "StudioOne" => ZoomShortcutStyle.StudioOne,
                "PremiereResolve" => ZoomShortcutStyle.PremiereResolve,
                "AvidMediaComposer" => ZoomShortcutStyle.AvidMediaComposer,
                "Custom" => ZoomShortcutStyle.Custom,
                _ => ZoomShortcutStyle.ProTools
            };
        }

        // カスタム設定
        if (CustomInKeyComboBox.SelectedItem is string inKeyStr && Enum.TryParse<VirtualKey>(inKeyStr, out var inKey))
        {
            settings.CustomZoomInKey = inKey;
        }
        settings.CustomZoomInCtrl = CustomInCtrlCheck.IsChecked == true;
        settings.CustomZoomInAlt = CustomInAltCheck.IsChecked == true;
        settings.CustomZoomInShift = CustomInShiftCheck.IsChecked == true;

        if (CustomOutKeyComboBox.SelectedItem is string outKeyStr && Enum.TryParse<VirtualKey>(outKeyStr, out var outKey))
        {
            settings.CustomZoomOutKey = outKey;
        }
        settings.CustomZoomOutCtrl = CustomOutCtrlCheck.IsChecked == true;
        settings.CustomZoomOutAlt = CustomOutAltCheck.IsChecked == true;
        settings.CustomZoomOutShift = CustomOutShiftCheck.IsChecked == true;

        settings.EnableNumpadZoom = EnableNumpadZoomToggle.IsOn;
        settings.EnableWheelZoom = EnableWheelZoomToggle.IsOn;

        // 保存先ディレクトリ設定
        settings.UseDefaultProjectDirectory = UseDefaultDirRadio.IsChecked == true;
        settings.DefaultProjectDirectory = DefaultDirTextBox.Text;

        // 自動保存バックアップ
        settings.EnableAutoSave = EnableAutoSaveToggle.IsOn;
        settings.AutoSaveIntervalMinutes = (int)Math.Clamp(AutoSaveIntervalBox.Value, 1, 120);
        settings.AutoSaveMaxBackups = (int)Math.Clamp(AutoSaveMaxBackupsBox.Value, 1, 99);

        // 保存
        SettingsManager.SaveSettings(settings);
    }
}
