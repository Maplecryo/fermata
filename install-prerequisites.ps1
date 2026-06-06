# Checks for and installs Rust and .NET 10 SDK on Windows.
# Run in PowerShell as your normal user (no admin required for user-level installs).
# Safe to run multiple times — skips anything already installed.

function Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "  [!]  $msg" -ForegroundColor Yellow }
function Info($msg) { Write-Host "  ->   $msg" }

Write-Host ""
Write-Host "Fermata - prerequisite installer"
Write-Host "================================="
Write-Host ""

# ── Rust ──────────────────────────────────────────────────────────────────────

if (Get-Command cargo -ErrorAction SilentlyContinue) {
    $rustVer = (cargo --version)
    Ok "Rust already installed ($rustVer)"
} else {
    Warn "Rust not found — downloading rustup installer"
    Info "This may take a minute..."
    $rustupUrl = "https://win.rustup.rs/x86_64"
    $rustupExe = "$env:TEMP\rustup-init.exe"
    Invoke-WebRequest -Uri $rustupUrl -OutFile $rustupExe -UseBasicParsing
    # -y accepts defaults, --no-modify-path so we handle PATH ourselves
    Start-Process -FilePath $rustupExe -ArgumentList "-y" -Wait -NoNewWindow
    Remove-Item $rustupExe

    # Add cargo to PATH for the rest of this script
    $env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"

    if (Get-Command cargo -ErrorAction SilentlyContinue) {
        Ok "Rust installed ($(cargo --version))"
    } else {
        Write-Host ""
        Warn "Rust installed but cargo is not in PATH yet."
        Info "Close this window and reopen PowerShell, then run this script again."
        exit 1
    }
}

# ── .NET 10 ───────────────────────────────────────────────────────────────────

$dotnetOk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetVer = (dotnet --version 2>$null)
    $major = [int]($dotnetVer -split '\.')[0]
    if ($major -ge 10) {
        Ok ".NET already installed ($dotnetVer)"
        $dotnetOk = $true
    } else {
        Warn ".NET found but version is $dotnetVer (need 10+) — will install .NET 10 alongside"
    }
}

if (-not $dotnetOk) {
    Info "Installing .NET 10 SDK via Microsoft install script..."
    $dotnetInstallScript = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $dotnetInstallScript -UseBasicParsing
    & $dotnetInstallScript -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
    Remove-Item $dotnetInstallScript

    # Add to PATH for the rest of this script
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"

    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Ok ".NET 10 installed ($(dotnet --version))"
    } else {
        Warn ".NET installed but not in PATH yet — reopen PowerShell and run this script again."
        exit 1
    }
}

Write-Host ""
Ok "All prerequisites satisfied — run .\build-app.ps1 to build Fermata"
Write-Host ""
