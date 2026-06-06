#!/bin/bash
# Builds Fermata.app — a double-clickable macOS app bundle.
# Automatically installs Rust and .NET 10 if they are missing.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"

# Install prerequisites if needed
bash "$ROOT/install-prerequisites.sh"

# Ensure cargo and dotnet are on PATH (in case they were just installed)
[ -f "$HOME/.cargo/env" ] && source "$HOME/.cargo/env"
[ -d "$HOME/.dotnet" ] && export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

APP="$ROOT/Fermata.app"
CONTENTS="$APP/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"

echo "==> Cleaning previous build"
rm -rf "$APP"

echo "==> Building fermata-monitor (Rust release)"
cargo build --release --manifest-path "$ROOT/Cargo.toml"

echo "==> Publishing fermata-ui (self-contained, osx-arm64)"
dotnet publish "$ROOT/fermata-ui/FermataUI.csproj" \
  -f net10.0 \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --nologo \
  -o "$ROOT/build/ui"

echo "==> Assembling Fermata.app"
mkdir -p "$MACOS" "$RESOURCES"

# App entry-point launcher
cat > "$MACOS/Fermata" << 'LAUNCHER'
#!/bin/bash
# Launcher: starts the monitor in the background, then the UI.
# When the UI exits, the monitor is also terminated.
DIR="$(cd "$(dirname "$0")" && pwd)"

# Redirect monitor logs to ~/Library/Logs/Fermata/monitor.log
LOG_DIR="$HOME/Library/Logs/Fermata"
mkdir -p "$LOG_DIR"

"$DIR/fermata-monitor" >> "$LOG_DIR/monitor.log" 2>&1 &
MONITOR_PID=$!

# Give the monitor a moment to create the IPC socket before the UI starts.
sleep 0.3

"$DIR/fermata-ui"

# UI has exited — shut down the monitor too.
kill "$MONITOR_PID" 2>/dev/null || true
wait "$MONITOR_PID" 2>/dev/null || true
LAUNCHER
chmod +x "$MACOS/Fermata"

# Binaries
cp "$ROOT/target/release/fermata-monitor" "$MACOS/fermata-monitor"
cp "$ROOT/build/ui/FermataUI" "$MACOS/fermata-ui" 2>/dev/null || \
cp "$ROOT/build/ui/fermata-ui" "$MACOS/fermata-ui"
chmod +x "$MACOS/fermata-monitor" "$MACOS/fermata-ui"

# Info.plist
cat > "$CONTENTS/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
    "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Fermata</string>
    <key>CFBundleDisplayName</key>
    <string>Fermata</string>
    <key>CFBundleIdentifier</key>
    <string>com.fermata.app</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleExecutable</key>
    <string>Fermata</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSUIElement</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
</dict>
</plist>
PLIST

echo ""
echo "✓ Built: $APP"
echo ""
echo "To install: drag Fermata.app to /Applications"
echo "To run now: open \"$APP\""
echo ""
echo "Logs are written to ~/Library/Logs/Fermata/"
