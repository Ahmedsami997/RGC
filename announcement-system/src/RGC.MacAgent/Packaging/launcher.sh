#!/bin/bash
# RGC.app main executable: starts the .NET agent in Contents/Resources/app.
DIR="$(cd "$(dirname "$0")/../Resources/app" && pwd)"
if [ -z "${DOTNET_ROOT:-}" ]; then
    if ls -d /usr/local/share/dotnet/shared/Microsoft.NETCore.App/8.* >/dev/null 2>&1; then
        export DOTNET_ROOT=/usr/local/share/dotnet
    else
        export DOTNET_ROOT="$HOME/.dotnet"
    fi
fi
exec "$DIR/RGC.Agent" "$@"
