#!/bin/sh
# launch-macos.sh — Build and run Intune Commander on macOS
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/src/Intune.Commander.Desktop/Intune.Commander.Desktop.csproj"

# Detect CPU architecture
ARCH="$(uname -m)"
case "$ARCH" in
    arm64) RID="osx-arm64" ;;
    x86_64) RID="osx-x64" ;;
    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

PUBLISH_DIR="$REPO_ROOT/publish/$RID"
BINARY="$PUBLISH_DIR/Intune.Commander.Desktop"

# Publish if binary doesn't exist or --rebuild is passed
if [ ! -f "$BINARY" ] || [ "${1:-}" = "--rebuild" ]; then
    echo "Publishing Intune Commander for $RID..."
    dotnet publish "$PROJECT" \
        --configuration Release \
        --runtime "$RID" \
        --self-contained true \
        --output "$PUBLISH_DIR" \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true
    echo "Done."
fi

echo "Launching Intune Commander..."
exec "$BINARY"
