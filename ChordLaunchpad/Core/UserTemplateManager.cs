using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Core;

/// <summary>
/// ユーザー定義コード進行テンプレートの永続化（JSON保存・読み込み）・追加・削除を管理するクラス
/// </summary>
public class UserTemplateManager
{
    private static UserTemplateManager? _instance;
    public static UserTemplateManager Instance => _instance ??= new UserTemplateManager();

    private readonly List<ProgressionTemplate> _userTemplates = new();
    private string? _customFilePath;

    public string StorageFilePath
    {
        get
        {
            if (!string.IsNullOrEmpty(_customFilePath)) return _customFilePath;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "ChordLaunchpad", "UserTemplates.json");
        }
        set => _customFilePath = value;
    }

    public UserTemplateManager(string? customFilePath = null)
    {
        _customFilePath = customFilePath;
        Load();
    }

    /// <summary>
    /// 登録済みのユーザー定義テンプレート一覧
    /// </summary>
    public IReadOnlyList<ProgressionTemplate> UserTemplates => _userTemplates.AsReadOnly();

    /// <summary>
    /// 組み込みの既定テンプレートとユーザー定義テンプレートを統合した全テンプレート一覧
    /// </summary>
    public IReadOnlyList<ProgressionTemplate> GetAllTemplates()
    {
        var list = new List<ProgressionTemplate>();
        // ユーザー定義テンプレートを上位に配置（アクセスしやすくする）
        list.AddRange(_userTemplates);
        list.AddRange(DefaultTemplates.Templates);
        return list;
    }

    /// <summary>
    /// 指定されたIDのテンプレートがユーザー定義のものかどうかを判定
    /// </summary>
    public bool IsUserTemplate(string id)
    {
        return _userTemplates.Any(t => t.Id == id);
    }

    /// <summary>
    /// 新規テンプレートを追加して保存
    /// </summary>
    public void AddTemplate(ProgressionTemplate template)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        
        // 既存の同一IDがあれば置換、なければ先頭に追加
        var existingIndex = _userTemplates.FindIndex(t => t.Id == template.Id);
        if (existingIndex >= 0)
        {
            _userTemplates[existingIndex] = template;
        }
        else
        {
            _userTemplates.Insert(0, template);
        }

        Save();
    }

    /// <summary>
    /// テンプレートを削除して保存
    /// </summary>
    public bool DeleteTemplate(string id)
    {
        var count = _userTemplates.RemoveAll(t => t.Id == id);
        if (count > 0)
        {
            Save();
            return true;
        }
        return false;
    }

    /// <summary>
    /// テンプレートをJSONから読み込み
    /// </summary>
    public void Load()
    {
        _userTemplates.Clear();
        try
        {
            var filePath = StorageFilePath;
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListProgressionTemplate);
                if (loaded != null)
                {
                    _userTemplates.AddRange(loaded);
                }
            }
        }
        catch
        {
            // 読み込みエラー時は空のまま開始
        }
    }

    /// <summary>
    /// テンプレートをJSONに保存
    /// </summary>
    public void Save()
    {
        try
        {
            var filePath = StorageFilePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_userTemplates, AppJsonContext.Default.ListProgressionTemplate);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // 保存エラー
        }
    }
}
