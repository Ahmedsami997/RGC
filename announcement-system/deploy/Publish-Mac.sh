#!/bin/bash
# Builds publish/RGC-Mac-Agent.zip: the macOS agent for Apple-silicon and Intel Macs plus its installer.
# Works on macOS, Linux or Windows (Git Bash/WSL) with the .NET 8 SDK.
set -euo pipefail
cd "$(dirname "$0")/.."
PROJECT=src/RGC.MacAgent/RGC.MacAgent.csproj
PKG=src/RGC.MacAgent/Packaging
OUT=publish/mac
STAGE="$OUT/RGC-Mac-Agent"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)

rm -rf "$OUT"
for rid in osx-arm64 osx-x64; do
    dotnet publish "$PROJECT" -c Release -r "$rid" --self-contained false -p:DebugType=none -o "$OUT/$rid" -nologo -v q
done

# Everything except the native launcher, the main assembly and its deps file is identical for both
# processors (Avalonia's dylibs are universal), so ship it once.
mkdir -p "$STAGE/payload/common" "$STAGE/payload/arm64" "$STAGE/payload/x64"
for f in "$OUT/osx-arm64"/*; do
    name=$(basename "$f")
    case "$name" in
        RGC.Agent|RGC.Agent.dll|RGC.Agent.deps.json) ;;
        *) cp -R "$f" "$STAGE/payload/common/" ;;
    esac
done
for f in RGC.Agent RGC.Agent.dll RGC.Agent.deps.json; do
    cp "$OUT/osx-arm64/$f" "$STAGE/payload/arm64/"
    cp "$OUT/osx-x64/$f" "$STAGE/payload/x64/"
done

sed "s/@VERSION@/$VERSION/g" "$PKG/Info.plist" > "$STAGE/payload/Info.plist"
cp "$PKG/rgc.icns" "$PKG/launcher.sh" "$STAGE/payload/"
cp "$PKG/install.sh" "$PKG/uninstall.sh" "$PKG/Install RGC.command" "$STAGE/"
chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh" "$STAGE/Install RGC.command" \
         "$STAGE/payload/launcher.sh" "$STAGE/payload/arm64/RGC.Agent" "$STAGE/payload/x64/RGC.Agent"

rm -f publish/RGC-Mac-Agent.zip
(cd "$OUT" && zip -qr -X ../RGC-Mac-Agent.zip RGC-Mac-Agent)
echo "Built publish/RGC-Mac-Agent.zip (version $VERSION)"
