using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ChordLaunchpad.Launcher;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string appDir = Path.Combine(baseDir, "app");
            string targetExe = Path.Combine(appDir, "ChordLaunchpad.exe");

            if (!File.Exists(targetExe))
            {
                targetExe = Path.Combine(baseDir, "ChordLaunchpad.exe");
                appDir = baseDir;
            }

            if (File.Exists(targetExe))
            {
                var startInfo = new ProcessStartInfo(targetExe)
                {
                    WorkingDirectory = appDir,
                    UseShellExecute = true
                };

                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        startInfo.ArgumentList.Add(arg);
                    }
                }

                Process.Start(startInfo);
            }
            else
            {
                MessageBox(
                    IntPtr.Zero,
                    "実行ファイル (ChordLaunchpad.exe) が見つかりませんでした。\n'app' フォルダ内にファイルが存在することをご確認ください。",
                    "ChordLaunchpad",
                    0x10);
            }
        }
        catch (Exception ex)
        {
            MessageBox(
                IntPtr.Zero,
                $"起動中にエラーが発生しました:\n{ex.Message}",
                "ChordLaunchpad エラー",
                0x10);
        }
    }
}
