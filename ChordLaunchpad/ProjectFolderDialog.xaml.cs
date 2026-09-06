using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad;

public sealed partial class ProjectFolderDialog : ContentDialog
{
    private readonly bool _isNewProject;
    private static string? _lastUsedParentDirectory;

    public string ProjectName { get; private set; } = string.Empty;
    public string ParentDirectory { get; private set; } = string.Empty;
    public string TargetProjectDirectory => Path.Combine(ParentDirectory, ProjectName);
    public string TargetProjectFilePath => Path.Combine(TargetProjectDirectory, $"{ProjectName}.chord");
    public string TargetBackupDirectory => Path.Combine(TargetProjectDirectory, "Project Backup");
    public string TargetMidiDirectory => Path.Combine(TargetProjectDirectory, "Chord MIDI");

    public ProjectFolderDialog(string? defaultProjectName = null, bool isNewProject = false)
    {
        InitializeComponent();
        _isNewProject = isNewProject;

        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        Title = isNewProject ? (isEn ? "Create New Project" : "新規プロジェクトの作成") : (isEn ? "Save Project" : "プロジェクトの保存");
        PrimaryButtonText = isNewProject ? (isEn ? "Create" : "新規作成") : (isEn ? "Save" : "保存");
        CloseButtonText = isEn ? "Cancel" : "キャンセル";

        DialogDescriptionText.Text = isEn
            ? "Create a dedicated project folder to organize project files, auto-backups, and MIDI exports."
            : "プロジェクト専用のフォルダを作成し、プロジェクトファイルや自動バックアップ・MIDI保存先を整理して格納します。";
        ProjectNameLabel.Text = isEn ? "Project Name (Folder & File name)" : "プロジェクト名 (フォルダ名・ファイル名)";
        ProjectNameTextBox.PlaceholderText = isEn ? "e.g. MyProgression_01" : "例: MyProgression_01";
        ParentDirLabel.Text = isEn ? "Target Directory (Parent folder)" : "保存先ディレクトリ (親フォルダ)";
        BrowseParentDirButton.Content = isEn ? "Browse..." : "参照...";
        PreviewStructureTitleText.Text = isEn ? "Target Folder Structure" : "作成されるフォルダ構成";
        BackupFolderDescText.Text = isEn ? @"Project Backup\ (Automatic temporary backup folder)" : @"Project Backup\ (自動一時バックアップ格納フォルダ)";
        MidiFolderDescText.Text = isEn ? @"Chord MIDI\ (Default MIDI export folder)" : @"Chord MIDI\ (MIDIエクスポート既定保存フォルダ)";

        // 親ディレクトリの初期値決定
        var settings = SettingsManager.Current;
        if (settings.UseDefaultProjectDirectory && !string.IsNullOrWhiteSpace(settings.DefaultProjectDirectory))
        {
            ParentDirectory = settings.DefaultProjectDirectory;
        }
        else if (!string.IsNullOrWhiteSpace(_lastUsedParentDirectory))
        {
            ParentDirectory = _lastUsedParentDirectory;
        }
        else
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ParentDirectory = Path.Combine(docs, "ChordLaunchpad Projects");
        }

        ParentDirTextBox.Text = ParentDirectory;

        // プロジェクト名の初期値決定
        if (!string.IsNullOrWhiteSpace(defaultProjectName))
        {
            ProjectName = defaultProjectName;
        }
        else
        {
            ProjectName = GenerateUniqueProjectName(ParentDirectory);
        }

        ProjectNameTextBox.Text = ProjectName;
        UpdatePreviewAndValidation();
    }

    private string GenerateUniqueProjectName(string parentDir)
    {
        var baseName = "MyProgression";
        int index = 1;
        while (true)
        {
            var name = $"{baseName}_{index:D2}";
            var folder = Path.Combine(parentDir, name);
            if (!Directory.Exists(folder))
            {
                return name;
            }
            index++;
        }
    }

    private void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ProjectName = ProjectNameTextBox.Text.Trim();
        UpdatePreviewAndValidation();
    }

    private void ParentDirTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ParentDirectory = ParentDirTextBox.Text.Trim();
        UpdatePreviewAndValidation();
    }

    private async void BrowseParentDir_Click(object sender, RoutedEventArgs e)
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
                ParentDirectory = folder.Path;
                ParentDirTextBox.Text = folder.Path;
                _lastUsedParentDirectory = folder.Path;
                UpdatePreviewAndValidation();
            }
        }
        catch (Exception ex)
        {
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = $"フォルダ選択エラー: {ex.Message}";
        }
    }

    private void UpdatePreviewAndValidation()
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;

        // プレビュー表示更新
        if (string.IsNullOrWhiteSpace(ProjectName) || string.IsNullOrWhiteSpace(ParentDirectory))
        {
            PreviewFolderPathText.Text = isEn ? "(Folder not determined)" : "(フォルダ未決定)";
            PreviewProjectFileText.Text = isEn ? "(File not determined)" : "(ファイル未決定)";
            IsPrimaryButtonEnabled = false;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Warning;
            ValidationInfoBar.Message = isEn
                ? "Please enter project name and target directory."
                : "プロジェクト名と保存先ディレクトリを入力してください。";
            return;
        }

        // 不正文字チェック
        var invalidChars = Path.GetInvalidFileNameChars();
        if (ProjectName.IndexOfAny(invalidChars) >= 0)
        {
            PreviewFolderPathText.Text = isEn ? "(Invalid file name)" : "(不正なファイル名)";
            PreviewProjectFileText.Text = isEn ? "(Invalid file name)" : "(不正なファイル名)";
            IsPrimaryButtonEnabled = false;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = isEn
                ? "Project name contains invalid characters (\\ / : * ? \" < > | etc.)."
                : "プロジェクト名に使用できない記号が含まれています (\\ / : * ? \" < > | など)。";
            return;
        }

        var folderPath = TargetProjectDirectory;
        PreviewFolderPathText.Text = $"{folderPath}\\";
        PreviewProjectFileText.Text = isEn
            ? $"{ProjectName}.chord (Project file)"
            : $"{ProjectName}.chord (プロジェクト本体ファイル)";

        // 既存フォルダ重複チェック
        if (Directory.Exists(folderPath))
        {
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Warning;
            ValidationInfoBar.Message = isEn
                ? "※ A folder with this name already exists. You can overwrite or choose another name."
                : "※ 同名のプロジェクトフォルダが既に存在します。上書き保存するか別の名前に変更してください。";
            IsPrimaryButtonEnabled = true;
        }
        else
        {
            ValidationInfoBar.IsOpen = false;
            IsPrimaryButtonEnabled = true;
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(ProjectName) || string.IsNullOrWhiteSpace(ParentDirectory))
        {
            args.Cancel = true;
            return;
        }

        _lastUsedParentDirectory = ParentDirectory;

        try
        {
            // 親ディレクトリ作成（存在しない場合）
            if (!Directory.Exists(ParentDirectory))
            {
                Directory.CreateDirectory(ParentDirectory);
            }

            // プロジェクト専用フォルダ作成
            if (!Directory.Exists(TargetProjectDirectory))
            {
                Directory.CreateDirectory(TargetProjectDirectory);
            }

            // Project Backup フォルダ作成
            if (!Directory.Exists(TargetBackupDirectory))
            {
                Directory.CreateDirectory(TargetBackupDirectory);
            }

            // Chord MIDI フォルダ作成
            if (!Directory.Exists(TargetMidiDirectory))
            {
                Directory.CreateDirectory(TargetMidiDirectory);
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ValidationInfoBar.IsOpen = true;
            ValidationInfoBar.Severity = InfoBarSeverity.Error;
            ValidationInfoBar.Message = $"フォルダ作成に失敗しました: {ex.Message}";
        }
    }
}
