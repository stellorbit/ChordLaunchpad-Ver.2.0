[CmdletBinding()]
param (
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$NoTest
)

$ErrorActionPreference = "Stop"
$SolutionPath = Join-Path $PSScriptRoot "ChordLaunchpad.slnx"
$TestProjectPath = Join-Path $PSScriptRoot "ChordLaunchpad.Tests\ChordLaunchpad.Tests.csproj"
$TotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " [Step 1/3] クリーン (Clean) を実行中..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
$StepStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
dotnet clean $SolutionPath -p:Platform=$Platform -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) {
    Write-Error "クリーンに失敗しました。"
    exit 1
}
Write-Host "✔ クリーン完了 ($([math]::Round($StepStopwatch.Elapsed.TotalSeconds, 1)) 秒)" -ForegroundColor DarkCyan

Write-Host "
=========================================" -ForegroundColor Green
Write-Host " [Step 2/3] ビルド (Build) を実行中..." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
$StepStopwatch.Restart()
dotnet build $SolutionPath -p:Platform=$Platform -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) {
    Write-Error "ビルドに失敗しました。"
    exit 1
}
Write-Host "✔ ビルド完了 ($([math]::Round($StepStopwatch.Elapsed.TotalSeconds, 1)) 秒)" -ForegroundColor DarkGreen

if (-not $NoTest) {
    Write-Host "
=========================================" -ForegroundColor Yellow
    Write-Host " [Step 3/3] テスト (Test) を実行中 (高速 --no-build)..." -ForegroundColor Yellow
    Write-Host "=========================================" -ForegroundColor Yellow
    $StepStopwatch.Restart()
    dotnet test $TestProjectPath -p:Platform=$Platform -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "テストに失敗しました。"
        exit 1
    }
    Write-Host "✔ テスト完了 ($([math]::Round($StepStopwatch.Elapsed.TotalSeconds, 1)) 秒)" -ForegroundColor DarkYellow
}

Write-Host "
✔ 全ステップが正常に完了しました！ (総所要時間: $([math]::Round($TotalStopwatch.Elapsed.TotalSeconds, 1)) 秒)" -ForegroundColor Green

