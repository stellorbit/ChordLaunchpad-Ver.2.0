using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ChordLaunchpad.Core;

/// <summary>
/// パストラバーサル攻撃や予約名不正アクセスを防ぐための安全なファイルパス検証・サニタイズヘルパー
/// </summary>
public static class SecurityPathHelper
{
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// ファイル名が Windows の予約デバイス名（CON, PRN, AUX, NUL, COM1-9, LPT1-9 等）に該当するか判定
    /// </summary>
    public static bool IsReservedDeviceName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).Trim();
        return ReservedDeviceNames.Any(r => string.Equals(r, nameWithoutExt, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ファイル名が安全で有効な形式か判定（パストラバーサル、無効文字、予約名、長さを検査）
    /// </summary>
    public static bool IsValidFileName(string? fileName, int maxLength = 128)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName.Length > maxLength) return false;

        // パス区切り文字や親ディレクトリ参照が含まれていないか
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            return false;
        }

        // 制御文字（NULL文字や非表示文字）が含まれていないか
        if (fileName.Any(c => c < 32 || c == 127))
        {
            return false;
        }

        // 不正な文字が含まれていないか
        var invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.IndexOfAny(invalidChars) >= 0)
        {
            return false;
        }

        // 予約デバイス名チェック
        if (IsReservedDeviceName(fileName))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// ファイル名を安全にサニタイズ（制御文字除去、パストラバーサル除去、不正文字置換、予約名回避、長さ制限）
    /// </summary>
    public static string SanitizeFileName(string? rawFileName, string defaultName = "output.mid", int maxLength = 128)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return defaultName;
        }

        // 1. まず制御文字（NULLバイト \0 や改行等）を完全に除去
        var noControls = new string(rawFileName.Where(c => c >= 32 && c != 127).ToArray());
        if (string.IsNullOrWhiteSpace(noControls))
        {
            return defaultName;
        }

        // 2. パス要素（ディレクトリ部分）を剥ぎ取り、ファイル名単体のみを抽出
        var fileName = Path.GetFileName(noControls).Trim();

        // 3. 不正文字のアンダースコア置換
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }

        // 4. 先頭・末尾のスペースやドットのトリム（Windows で問題となる形式の回避）
        fileName = fileName.Trim(' ', '.');

        // 5. 長さの切り詰め
        if (fileName.Length > maxLength)
        {
            var ext = Path.GetExtension(fileName);
            var baseLen = Math.Max(1, maxLength - ext.Length);
            fileName = fileName[..baseLen] + ext;
        }

        // 空になった場合はデフォルト名
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return defaultName;
        }

        // 6. 予約デバイス名の場合はプレフィックスを付与して無害化
        if (IsReservedDeviceName(fileName))
        {
            fileName = $"safe_{fileName}";
        }

        return fileName;
    }

    /// <summary>
    /// 同一ディレクトリ内に一時ファイルを作成して書き込み、アトミックに目的ファイルへ置換する安全なファイル書き込み（同期版）
    /// </summary>
    public static void WriteAllTextAtomic(string filePath, string content)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = Path.Combine(dir ?? string.Empty, $".{Path.GetFileName(fullPath)}.tmp_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* 安全に無視 */ }
            }
        }
    }

    /// <summary>
    /// 同一ディレクトリ内に一時ファイルを作成して書き込み、アトミックに目的ファイルへ置換する安全なファイル書き込み（非同期版）
    /// </summary>
    public static async Task WriteAllTextAtomicAsync(string filePath, string content)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = Path.Combine(dir ?? string.Empty, $".{Path.GetFileName(fullPath)}.tmp_{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(tempPath, content);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* 安全に無視 */ }
            }
        }
    }
}
