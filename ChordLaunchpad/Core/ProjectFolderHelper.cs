using System;
using System.IO;

namespace ChordLaunchpad.Core;

/// <summary>
/// プロジェクトフォルダおよびファイル構成の管理ヘルパー
/// </summary>
public static class ProjectFolderHelper
{
    public const string DefaultProjectBaseName = "MyProgression";
    public const string BackupFolderName = "Project Backup";
    public const string MidiFolderName = "Chord MIDI";
    public const string ProjectExtension = ".chord";

    /// <summary>
    /// 親ディレクトリ内で重複しないプロジェクトフォルダ名を生成します (例: MyProgression_01)
    /// </summary>
    public static string GenerateUniqueProjectName(string parentDir, string baseName = DefaultProjectBaseName)
    {
        if (string.IsNullOrWhiteSpace(parentDir))
        {
            parentDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordLaunchpad Projects");
        }

        var safeBaseName = SecurityPathHelper.SanitizeFileName(baseName, DefaultProjectBaseName, 64);
        int index = 1;
        while (index < 10000)
        {
            var candidateName = $"{safeBaseName}_{index:D2}";
            var candidatePath = Path.Combine(parentDir, candidateName);
            if (!Directory.Exists(candidatePath))
            {
                return candidateName;
            }
            index++;
        }

        return $"{safeBaseName}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// プロジェクトフォルダ配下の標準ディレクトリ構成を生成します
    /// </summary>
    public static (string projectDirectory, string projectFilePath, string backupDirectory, string midiDirectory) CreateProjectStructure(string parentDir, string projectName)
    {
        if (!Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var projectDir = Path.Combine(parentDir, projectName);
        var backupDir = Path.Combine(projectDir, BackupFolderName);
        var midiDir = Path.Combine(projectDir, MidiFolderName);
        var projectFile = Path.Combine(projectDir, $"{projectName}{ProjectExtension}");

        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        if (!Directory.Exists(midiDir))
        {
            Directory.CreateDirectory(midiDir);
        }

        return (projectDir, projectFile, backupDir, midiDir);
    }
}
