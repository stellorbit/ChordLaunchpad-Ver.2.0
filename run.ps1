[CmdletBinding()]
param (
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProjectPath = "H:\Antigravity\ChordLaunchpad\ChordLaunchpad.csproj"

if ($Clean) {
    Write-Host "クリーンを実行しています..." -ForegroundColor Cyan
    dotnet clean $ProjectPath -p:Platform=$Platform -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

Write-Host "ChordLaunchpad をビルドしています..." -ForegroundColor Green
dotnet build $ProjectPath -p:Platform=$Platform -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) {
    Write-Host "増分ビルドに失敗しました。自動クリーン後に再ビルドします..." -ForegroundColor Yellow
    dotnet clean $ProjectPath -p:Platform=$Platform -c $Configuration -m:1
    dotnet build $ProjectPath -p:Platform=$Platform -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$FoundExe = Get-ChildItem -Path "H:\Antigravity\ChordLaunchpad\bin" -Filter "ChordLaunchpad.exe" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($FoundExe) {
    $ExePath = $FoundExe.FullName
    Write-Host "ChordLaunchpad を起動しています: $ExePath" -ForegroundColor Cyan
    Start-Process -FilePath $ExePath
} else {
    Write-Error "実行可能ファイル (ChordLaunchpad.exe) が見つかりません。"
    exit 1
}
