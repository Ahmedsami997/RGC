#!/bin/bash
# Installs the RGC Announcements agent for the current macOS user.
#   - puts RGC.app in /Applications (or ~/Applications if you are not an admin)
#   - installs the .NET 8 runtime for this user if the Mac doesn't have it
#   - starts RGC at every login and keeps it running
# Run from Terminal:   bash install.sh
set -euo pipefail
cd "$(dirname "$0")"
PAYLOAD="$PWD/payload"
LABEL="com.royalgolfclub.rgc.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG="$HOME/Library/Logs/RGC"
mkdir -p "$LOG"

say() { printf '\033[1;32m%s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m%s\033[0m\n' "$*"; exit 1; }

[ -d "$PAYLOAD/common" ] || fail "The 'payload' folder is missing. Unzip the whole RGC-Mac-Agent.zip and run install.sh from inside it."

case "$(uname -m)" in
    arm64) ARCH=arm64 ;;
    x86_64) ARCH=x64 ;;
    *) fail "Unsupported Mac processor: $(uname -m)" ;;
esac

# 1. .NET 8 runtime: use the system one if present, otherwise install it just for this user.
has_runtime() { ls -d "$1/shared/Microsoft.NETCore.App/8."* >/dev/null 2>&1; }
if has_runtime /usr/local/share/dotnet; then
    DOTNET_ROOT_DIR=/usr/local/share/dotnet
else
    DOTNET_ROOT_DIR="$HOME/.dotnet"
    if ! has_runtime "$DOTNET_ROOT_DIR"; then
        say "Installing the .NET 8 runtime for $USER (one time, about 30 MB)..."
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir "$DOTNET_ROOT_DIR" --no-path
    fi
fi
say ".NET runtime: $DOTNET_ROOT_DIR"

# 2. Stop a running copy before replacing it.
launchctl bootout "gui/$(id -u)" "$PLIST" 2>/dev/null || true
pkill -x RGC.Agent 2>/dev/null || true

# 3. Build RGC.app.
if [ -w /Applications ]; then APPS=/Applications; else APPS="$HOME/Applications"; mkdir -p "$APPS"; fi
APP="$APPS/RGC.app"
BIN="$APP/Contents/Resources/app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$BIN"
cp -R "$PAYLOAD/common/." "$BIN/"
cp -R "$PAYLOAD/$ARCH/." "$BIN/"
cp "$PAYLOAD/launcher.sh" "$APP/Contents/MacOS/RGC"
cp "$PAYLOAD/Info.plist" "$APP/Contents/Info.plist"
cp "$PAYLOAD/rgc.icns" "$APP/Contents/Resources/rgc.icns"
chmod +x "$APP/Contents/MacOS/RGC" "$BIN/RGC.Agent"

# Downloaded files are quarantined, and Apple-silicon Macs only run signed code:
# clear the quarantine flag and give each program file a local (ad-hoc) signature.
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
for f in "$BIN/RGC.Agent" "$BIN"/*.dylib; do
    codesign --force --sign - "$f" >/dev/null 2>&1 || fail "Could not sign $f"
done
say "Installed $APP"

# 4. Start at login, restart if it stops.
mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$LABEL</string>
    <key>ProgramArguments</key>
    <array><string>$APP/Contents/MacOS/RGC</string></array>
    <key>EnvironmentVariables</key>
    <dict><key>DOTNET_ROOT</key><string>$DOTNET_ROOT_DIR</string></dict>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><true/>
    <key>ThrottleInterval</key><integer>15</integer>
    <key>ProcessType</key><string>Interactive</string>
    <key>LimitLoadToSessionType</key><string>Aqua</string>
    <key>StandardOutPath</key><string>$LOG/launchd.log</string>
    <key>StandardErrorPath</key><string>$LOG/launchd.log</string>
</dict>
</plist>
PLISTEOF
launchctl bootstrap "gui/$(id -u)" "$PLIST"
say "RGC is running. Look for the RGC crown in the menu bar (top right)."
echo "A sign-in window will open: sign in once with your work email."
echo "Logs: $LOG"
