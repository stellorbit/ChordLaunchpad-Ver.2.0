using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad;

public sealed partial class InitialFolderSetupDialog : ContentDialog
{
    public string SelectedFolderPath { get; private set; } = string.Empty;

    public InitialFolderSetupDialog()
    {
        InitializeComponent();

        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        Title = isEn ? "Initial Setup: Project Location" : "初期設定: プロジェクト保存先フォルダー";
        PrimaryButtonText = isEn ? "Set & Start" : "設定して開始";
        CloseButtonText = isEn ? "Use Default Folder" : "既定フォルダーで開始";

        WelcomeTitleText.Text = isEn ? "Welcome to ChordLaunchpad" : "ChordLaunchpad へようこそ";
        WelcomeDescriptionText.Text = isEn
            ? "To ensure a smooth music creation workflow, please specify the root directory for your projects, automatic backups, and exported MIDI files. A dedicated project folder will be automatically created under this directory upon startup."
            : "楽曲制作をスムーズに行うため、プロジェクトや自動バックアップ、MIDIファイルを保存する基本フォルダーを指定してください。起動時にこのフォルダー配下へプロジェクト専用フォルダーが自動生成されます。";
        FolderSelectLabel.Text = isEn ? "Project Root Directory (Parent Folder)" : "プロジェクト格納先フォルダー (親ディレクトリ)";
        BrowseFolderButton.Content = isEn ? "Browse..." : "参照...";
        NoticeText.Text = isEn
            ? "※ You can change the project directory anytime in the [Settings] dialog."
            : "※ 保存先フォルダーは後から [設定] ダイアログでもいつでも変更可能です。";
        PreviewTitleText.Text = isEn ? "Folder Structure Created Automatically on Startup" : "起動時に自動生成される構成";

        // 既定の保存先パスを取得
        var settings = SettingsManager.Current;
        var defaultPath = !string.IsNullOrWhiteSpace(settings.DefaultProjectDirectory)
            ? settings.DefaultProjectDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordLaunchpad Projects");

        SelectedFolderPath = defaultPath;
        FolderPathTextBox.Text = SelectedFolderPath;
        UpdatePreviewAndValidation();
    }

    private void FolderPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SelectedFolderPath = FolderPathTextBox.Text.Trim();
        UpdatePreviewAndValidation();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
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
                SelectedFolderPath = folder.Path;
                FolderPathTextBox.Text = folder.Path;
                UpdatePreviewAndValidation();
            }
        }
        catch (Exception ex)
        {
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = $"フォルダー選択エラー: {ex.Message}";
        }
    }

    private void UpdatePreviewAndValidation()
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;

        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            PreviewFolderText.Text = isEn ? "(Folder not specified)" : "(フォルダー未指定)";
            IsPrimaryButtonEnabled = false;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Warning;
            ValidationInfoBar.Message = isEn
                ? "Please specify a valid folder path."
                : "有効なフォルダーパスを指定してください。";
            return;
        }

        try
        {
            // パスの妥当性チェック
            var fullPath = Path.GetFullPath(SelectedFolderPath);
            PreviewFolderText.Text = Path.Combine(fullPath, "MyProgression_01") + "\\";
            ValidationInfoBar.IsOpen = false;
            IsPrimaryButtonEnabled = true;
        }
        catch (Exception)
        {
            PreviewFolderText.Text = isEn ? "(Invalid path)" : "(無効なパス)";
            IsPrimaryButtonEnabled = false;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = isEn
                ? "Invalid folder path specified."
                : "無効なフォルダーパスが指定されています。";
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            args.Cancel = true;
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(SelectedFolderPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var settings = SettingsManager.Current;
            settings.DefaultProjectDirectory = fullPath;
            settings.UseDefaultProjectDirectory = true;
            settings.HasCompletedInitialSetup = true;
            SettingsManager.Save(settings);
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = $"フォルダー作成エラー: {ex.Message}";
        }
    }

    private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordLaunchpad Projects");
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            var settings = SettingsManager.Current;
            settings.DefaultProjectDirectory = defaultPath;
            settings.UseDefaultProjectDirectory = true;
            settings.HasCompletedInitialSetup = true;
            SettingsManager.Save(settings);
            SelectedFolderPath = defaultPath;
        }
        catch (Exception)
        {
            // フォールバック
        }
    }
}
