# Builds Fermata for Windows — produces a Fermata-Windows\ folder you can
# put anywhere and launch by double-clicking Fermata.bat.
# Run in PowerShell after running install-prerequisites.ps1.
param()
$ErrorActionPreference = "Stop"

$Root    = $PSScriptRoot
$OutDir  = "$Root\Fermata-Windows"

Write-Host "==> Cleaning previous build"
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null

Write-Host "==> Building fermata-monitor (Rust release)"
cargo build --release --manifest-path "$Root\Cargo.toml"

Write-Host "==> Publishing fermata-ui (self-contained, win-x64)"
dotnet publish "$Root\fermata-ui\FermataUI.csproj" `
    -f net10.0-windows `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --nologo `
    -o "$Root\build\ui-win"

Write-Host "==> Assembling Fermata-Windows\"

# Binaries
Copy-Item "$Root\target\release\fermata-monitor.exe" "$OutDir\fermata-monitor.exe"
Copy-Item "$Root\build\ui-win\FermataUI.exe"         "$OutDir\fermata-ui.exe"

# Launcher batch file — starts both processes, hides the monitor window
$bat = @'
@echo off
start "" /B "%~dp0fermata-monitor.exe"
start "" "%~dp0fermata-ui.exe"
'@
Set-Content -Path "$OutDir\Fermata.bat" -Value $bat -Encoding ASCII

Write-Host ""
Write-Host "Built: $OutDir" -ForegroundColor Green
Write-Host ""
Write-Host "To run: double-click Fermata-Windows\Fermata.bat"
Write-Host "To install: move the Fermata-Windows folder anywhere you like"
Write-Host "            and create a shortcut to Fermata.bat on your desktop."
Write-Host ""
