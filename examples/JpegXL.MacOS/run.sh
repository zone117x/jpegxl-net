#!/bin/bash
set -e

cd "$(dirname "$0")"

# Check if Xcode is installed (not just Command Line Tools)
XCODE_PATH=$(xcode-select -p 2>/dev/null)
if [[ "$XCODE_PATH" == *"CommandLineTools"* ]] && [[ ! -d "/Applications/Xcode.app" ]]; then
    echo "Error: Full Xcode installation is required (not just Command Line Tools)."
    echo ""
    echo "1. Install Xcode from the Mac App Store"
    echo "2. Run: sudo xcode-select -s /Applications/Xcode.app/Contents/Developer"
    exit 1
fi

# Check if macos workload is installed
if ! dotnet workload list 2>/dev/null | grep -q "macos"; then
    echo "The macOS workload is not installed."
    echo "Please run: sudo dotnet workload install macos"
    exit 1
fi

# Build
dotnet build

# Run the app
APP_PATH="bin/Debug/net10.0-macos/osx-arm64/JPEG XL Viewer.app"
if [[ -d "$APP_PATH" ]]; then
    if [[ -n "$1" ]]; then
        open "$APP_PATH" --args "$@"
    else
        open "$APP_PATH"
    fi
else
    echo "App bundle not found at: $APP_PATH"
    exit 1
fi
