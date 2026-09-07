[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$DistDir = Join-Path $ScriptDir "dist"
$PublishTemp = Join-Path $ScriptDir "publish_temp"
$LauncherTemp = Join-Path $ScriptDir "launcher_temp"
$PortableStage = Join-Path $ScriptDir "portable_stage"
$TotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# 出力・作業ディレクトリの初期化
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
if (Test-Path $PublishTemp) { Remove-Item -Recurse -Force $PublishTemp }
if (Test-Path $LauncherTemp) { Remove-Item -Recurse -Force $LauncherTemp }
if (Test-Path $PortableStage) { Remove-Item -Recurse -Force $PortableStage }

New-Item -ItemType Directory -Path $DistDir | Out-Null

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [Step 1/5] ChordLaunchpad 本体のパブリッシュ中 (Release)..." -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
$ProjPath = Join-Path $ScriptDir "ChordLaunchpad\ChordLaunchpad.csproj"
dotnet publish $ProjPath -c $Configuration -r win-x64 --self-contained true `
    -p:Platform=$Platform -p:PublishReadyToRun=true -p:PublishTrimmed=false `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:BuildInParallel=false `
    -o $PublishTemp

if ($LASTEXITCODE -ne 0) {
    Write-Error "本体のパブリッシュに失敗しました。"
    exit 1
}

# WinUI 3 必須アセット・リソースの配置保証 (PRI ファイル & Assets フォルダ)
$TargetBinDir = Join-Path $ScriptDir "ChordLaunchpad\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$PriSource = Join-Path $TargetBinDir "ChordLaunchpad.pri"
if (Test-Path $PriSource) {
    Copy-Item $PriSource (Join-Path $PublishTemp "ChordLaunchpad.pri") -Force
    Copy-Item $PriSource (Join-Path $PublishTemp "resources.pri") -Force
    Write-Host "✔ PRI リソース (ChordLaunchpad.pri, resources.pri) を配置しました。" -ForegroundColor DarkCyan
} else {
    Write-Warning "ChordLaunchpad.pri が bin ディレクトリに見つかりませんでした。"
}

$AssetsSource = Join-Path $ScriptDir "ChordLaunchpad\Assets"
if (Test-Path $AssetsSource) {
    Copy-Item -Path $AssetsSource -Destination (Join-Path $PublishTemp "Assets") -Recurse -Force
    Write-Host "✔ Assets フォルダを配置しました。" -ForegroundColor DarkCyan
}

Write-Host "
========================================================" -ForegroundColor Magenta
Write-Host " [Step 2/5] サテライト言語フォルダとデバッグシンボルのクリーンアップ中..." -ForegroundColor Magenta
Write-Host "========================================================" -ForegroundColor Magenta
# 日本語と英語以外の言語フォルダを完全削除
$KeepLanguages = @("ja", "ja-jp", "en", "en-us", "assets", "microsoft.ui.xaml")
$Dirs = Get-ChildItem -Path $PublishTemp -Directory
$RemovedCount = 0
foreach ($dir in $Dirs) {
    if ($KeepLanguages -notcontains $dir.Name.ToLowerInvariant()) {
        Remove-Item -Recurse -Force $dir.FullName
        $RemovedCount++
    }
}
# 配布サイズ削減のため .pdb を削除
Get-ChildItem -Path $PublishTemp -Filter "*.pdb" -Recurse | Remove-Item -Force
Write-Host "✔ 不要言語フォルダ ($RemovedCount 個) およびデバッグシンボルを削除しました。" -ForegroundColor DarkMagenta

Write-Host "
========================================================" -ForegroundColor Blue
Write-Host " [Step 3/5] 超軽量ネイティブ起動ランチャー (AOT) をビルド中..." -ForegroundColor Blue
Write-Host "========================================================" -ForegroundColor Blue
$LauncherProj = Join-Path $ScriptDir "ChordLaunchpad.Launcher\ChordLaunchpad.Launcher.csproj"
dotnet publish $LauncherProj -r win-x64 -c Release -p:Platform=x64 -o $LauncherTemp
if ($LASTEXITCODE -ne 0) {
    Write-Error "ランチャーのビルドに失敗しました。"
    exit 1
}

Write-Host "
========================================================" -ForegroundColor Yellow
Write-Host " [Step 4/5] スリムポータブル版 ZIP を生成中..." -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Yellow
$PortableFolder = Join-Path $PortableStage "ChordLaunchpad_v2.0.0_Portable"
$AppFolder = Join-Path $PortableFolder "app"
New-Item -ItemType Directory -Path $AppFolder | Out-Null

# ランチャーをルートに ChordLaunchpad.exe として配置
Copy-Item (Join-Path $LauncherTemp "ChordLaunchpad.Launcher.exe") (Join-Path $PortableFolder "ChordLaunchpad.exe")

# README の配置
$ReadmeContent = @"
========================================================================
 ChordLaunchpad Ver 2.0 (ポータブル版)
========================================================================

【起動方法】
このフォルダーにある 「ChordLaunchpad.exe」 をダブルクリックしてください。

【注意】
- 「app」フォルダーの中にはアプリケーションの実行に必要なシステムファイルが
  格納されています。フォルダーの名前変更や移動・削除は行わないでください。

【公式サイト・リポジトリ】
https://github.com/stellorbit/ChordLaunchpad-Ver.2.0
========================================================================
"@
Set-Content -Path (Join-Path $PortableFolder "はじめにお読みください.txt") -Value $ReadmeContent -Encoding utf8

# 本体ファイルを app フォルダへ移動/コピー
Copy-Item -Path "$PublishTemp\*" -Destination $AppFolder -Recurse -Force

# ZIP 圧縮
$ZipOutput = Join-Path $DistDir "ChordLaunchpad_v2.0.0_Portable_win-x64.zip"
Compress-Archive -Path "$PortableFolder\*" -DestinationPath $ZipOutput -CompressionLevel Optimal
Write-Host "✔ ポータブル版 ZIP 生成完了: $ZipOutput" -ForegroundColor DarkYellow

Write-Host "
========================================================" -ForegroundColor Green
Write-Host " [Step 5/5] Inno Setup によるインストーラー (Setup.exe) 生成中..." -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
$ISCC = "C:\Users\猛攻型ことねP\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $ISCC)) {
    $found = Get-ChildItem -Path "C:\Program Files", "C:\Program Files (x86)", "$env:LOCALAPPDATA\Programs" -Filter "ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $ISCC = $found.FullName }
}

if (Test-Path $ISCC) {
    & $ISCC (Join-Path $ScriptDir "installer.iss")
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✔ インストーラー生成完了: dist\ChordLaunchpad_v2.0.0_Setup.exe" -ForegroundColor DarkGreen
    } else {
        Write-Error "Inno Setup のコンパイルに失敗しました。"
        exit 1
    }
} else {
    Write-Warning "ISCC.exe が見つからなかったため、インストーラー生成をスキップしました。"
}

# 一時作業フォルダのクリーンアップ
Remove-Item -Recurse -Force $PublishTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $LauncherTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $PortableStage -ErrorAction SilentlyContinue

Write-Host "
========================================================" -ForegroundColor Cyan
Write-Host "🎉 全てのパッケージ生成が完了しました！ (総所要時間: $([math]::Round($TotalStopwatch.Elapsed.TotalSeconds, 1)) 秒)" -ForegroundColor Cyan
Get-ChildItem $DistDir | Select-Object Name, @{Name="Size(MB)";Expression={[math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
Write-Host "========================================================" -ForegroundColor Cyan
