#!/bin/bash
# Checks for and installs Rust and .NET 10 SDK on macOS.
# Safe to run multiple times — skips anything already installed.
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

ok()   { echo -e "${GREEN}  ✓ $*${NC}"; }
warn() { echo -e "${YELLOW}  ! $*${NC}"; }
info() { echo -e "  → $*"; }

echo ""
echo "Fermata — prerequisite installer"
echo "================================="
echo ""

# ── Rust ──────────────────────────────────────────────────────────────────────

if command -v cargo &>/dev/null; then
    ok "Rust already installed ($(cargo --version))"
else
    warn "Rust not found — installing via rustup"
    info "This may take a minute..."
    curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --no-modify-path
    # Source the env so cargo is available in the rest of this script
    source "$HOME/.cargo/env"
    ok "Rust installed ($(cargo --version))"
fi

# ── .NET 10 ───────────────────────────────────────────────────────────────────

DOTNET_OK=false
if command -v dotnet &>/dev/null; then
    DOTNET_VER=$(dotnet --version 2>/dev/null || echo "0")
    MAJOR="${DOTNET_VER%%.*}"
    if [ "$MAJOR" -ge 10 ] 2>/dev/null; then
        ok ".NET already installed ($DOTNET_VER)"
        DOTNET_OK=true
    else
        warn ".NET found but version is $DOTNET_VER (need 10+) — will install .NET 10 alongside"
    fi
fi

if [ "$DOTNET_OK" = false ]; then
    info "Installing .NET 10 SDK via Microsoft install script..."
    DOTNET_INSTALL_DIR="$HOME/.dotnet"
    mkdir -p "$DOTNET_INSTALL_DIR"
    # Download the official install script
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_INSTALL_DIR"
    rm /tmp/dotnet-install.sh

    # Add to PATH for the rest of this script
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"

    ok ".NET 10 installed ($(dotnet --version))"
    echo ""
    warn "Add .NET to your PATH permanently by adding these lines to your ~/.zshrc or ~/.bash_profile:"
    echo ""
    echo '    export DOTNET_ROOT="$HOME/.dotnet"'
    echo '    export PATH="$HOME/.dotnet:$PATH"'
    echo ""
    info "Then run: source ~/.zshrc"
    echo ""
fi

echo ""
ok "All prerequisites satisfied — run ./build-app.sh to build Fermata.app"
echo ""
