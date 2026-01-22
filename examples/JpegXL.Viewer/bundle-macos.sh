#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="JPEG XL Viewer"
BUNDLE_NAME="$APP_NAME.app"
PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net8.0/osx-arm64/publish"

# Check if publish directory exists
if [ ! -d "$PUBLISH_DIR" ]; then
    echo "Publish directory not found. Run first:"
    echo "  dotnet publish -c Release -r osx-arm64 --self-contained"
    exit 1
fi

# Create bundle structure
BUNDLE_DIR="$SCRIPT_DIR/$BUNDLE_NAME"
rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/Contents/MacOS"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

# Copy published files
cp -R "$PUBLISH_DIR/"* "$BUNDLE_DIR/Contents/MacOS/"

# Copy Info.plist
cp "$SCRIPT_DIR/Info.plist" "$BUNDLE_DIR/Contents/"

# Make executable
chmod +x "$BUNDLE_DIR/Contents/MacOS/JpegXL.Viewer"

echo "✅ Created: $BUNDLE_DIR"
echo ""
echo "To install, run:"
echo "  cp -R \"$BUNDLE_DIR\" /Applications/"
echo ""
echo "Or just double-click the .app to run it."
