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

# icon.png からの Windows 完全準拠マルチ解像度 AppIcon.ico (16〜128: DIB, 256: PNG) 更新
$PngSource = Join-Path $ScriptDir "icon.png"
if (Test-Path $PngSource) {
    Add-Type -AssemblyName System.Drawing
    $srcBmp = [System.Drawing.Bitmap]::FromFile($PngSource)
    $icoPath = Join-Path $ScriptDir "ChordLaunchpad\Assets\AppIcon.ico"
    $sizes = @(16, 24, 32, 48, 64, 128, 256)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)
    for ($i = 0; $i -lt ($sizes.Count * 16); $i++) { $bw.Write([byte]0) }

    $entryIdx = 0
    foreach ($sz in $sizes) {
        $destBmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($destBmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($srcBmp, 0, 0, $sz, $sz)
        $g.Dispose()

        [byte[]]$imgData = $null
        if ($sz -eq 256) {
            $pngMs = New-Object System.IO.MemoryStream
            $destBmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
            $imgData = $pngMs.ToArray()
            $pngMs.Dispose()
        } else {
            $w = $sz; $h = $sz
            $andStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
            $andMaskSize = $andStride * $h
            $xorSize = $w * $h * 4
            $dibMs = New-Object System.IO.MemoryStream(40 + $xorSize + $andMaskSize)
            $dibBw = New-Object System.IO.BinaryWriter($dibMs)
            $dibBw.Write([uint32]40)
            $dibBw.Write([int32]$w)
            $dibBw.Write([int32]($h * 2))
            $dibBw.Write([uint16]1)
            $dibBw.Write([uint16]32)
            $dibBw.Write([uint32]0)
            $dibBw.Write([uint32]($xorSize + $andMaskSize))
            $dibBw.Write([int32]0); $dibBw.Write([int32]0)
            $dibBw.Write([uint32]0); $dibBw.Write([uint32]0)

            $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
            $bmpData = $destBmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $stride = $bmpData.Stride
            $pixelBytes = New-Object byte[] ($stride * $h)
            [System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $pixelBytes, 0, $pixelBytes.Length)
            $destBmp.UnlockBits($bmpData)

            for ($y = $h - 1; $y -ge 0; $y--) {
                $rowOffset = $y * $stride
                for ($x = 0; $x -lt $w; $x++) {
                    $pxOffset = $rowOffset + ($x * 4)
                    $dibBw.Write($pixelBytes[$pxOffset])
                    $dibBw.Write($pixelBytes[$pxOffset + 1])
                    $dibBw.Write($pixelBytes[$pxOffset + 2])
                    $dibBw.Write($pixelBytes[$pxOffset + 3])
                }
            }

            for ($y = $h - 1; $y -ge 0; $y--) {
                $rowOffset = $y * $stride
                $curByte = [byte]0; $bit = 0; $written = 0
                for ($x = 0; $x -lt $w; $x++) {
                    if ($pixelBytes[$rowOffset + ($x * 4) + 3] -eq 0) {
                        $curByte = $curByte -bor [byte](1 -shl (7 - $bit))
                    }
                    $bit++
                    if ($bit -eq 8) {
                        $dibBw.Write($curByte); $written++; $curByte = [byte]0; $bit = 0
                    }
                }
                if ($bit -gt 0) { $dibBw.Write($curByte); $written++ }
                while ($written -lt $andStride) { $dibBw.Write([byte]0); $written++ }
            }
            $imgData = $dibMs.ToArray()
            $dibBw.Dispose()
            $dibMs.Dispose()
        }
        $destBmp.Dispose()

        $offset = $bw.BaseStream.Position
        $bw.BaseStream.Seek(6 + ($entryIdx * 16), [System.IO.SeekOrigin]::Begin) | Out-Null
        $bw.Write([byte]($(if ($sz -eq 256) { 0 } else { $sz })))
        $bw.Write([byte]($(if ($sz -eq 256) { 0 } else { $sz })))
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$imgData.Length)
        $bw.Write([uint32]$offset)

        $bw.BaseStream.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $bw.Write($imgData)
        $entryIdx++
    }
    $srcBmp.Dispose()
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
    Write-Host "✔ Windows 完全準拠のマルチ解像度 AppIcon.ico を配置・更新しました。" -ForegroundColor DarkCyan
}

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
$DestAssets = Join-Path $PublishTemp "Assets"
if (Test-Path $AssetsSource) {
    if (-not (Test-Path $DestAssets)) {
        New-Item -ItemType Directory -Path $DestAssets | Out-Null
    }
    Copy-Item -Path "$AssetsSource\*" -Destination $DestAssets -Recurse -Force
    Write-Host "✔ Assets フォルダを配置しました。" -ForegroundColor DarkCyan
}

Write-Host "
========================================================" -ForegroundColor Magenta
Write-Host " [Step 2/5] サテライト言語フォルダとデバッグシンボルのクリーンアップ中..." -ForegroundColor Magenta
Write-Host "========================================================" -ForegroundColor Magenta
# 日本語(ja-JP)と英語(en-US)以外の言語フォルダを完全削除 (jaなども削除)
$KeepLanguages = @("ja-jp", "en-us", "assets", "microsoft.ui.xaml")
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

# ZIP 圧縮 (フォルダごと圧縮して解凍時に親フォルダが保持されるようにする)
$ZipOutput = Join-Path $DistDir "ChordLaunchpad_v2.0.0_Portable_win-x64.zip"
Compress-Archive -Path $PortableFolder -DestinationPath $ZipOutput -CompressionLevel Optimal
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
