#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_FILE="$PROJECT_ROOT/native/jxl-rs/jxl/resources/test/conformance_test_images/animation_icos4d_5.jxl"

CONFIG="Debug"
KILL_AFTER=""
LOG_FILE=""
CUSTOM_FILE=""
EXTRA_ARGS=()

for arg in "$@"; do
    case $arg in
        --release)
            CONFIG="Release"
            ;;
        --kill-after=*)
            KILL_AFTER="${arg#*=}"
            ;;
        --log-file|--log)
            LOG_FILE="$SCRIPT_DIR/logs/run.txt"
            ;;
        --file=*)
            CUSTOM_FILE="${arg#*=}"
            ;;
        *)
            # First non-flag argument that is an existing file is treated as the file path
            if [[ -z "$CUSTOM_FILE" && -f "$arg" ]]; then
                CUSTOM_FILE="$arg"
            else
                # Pass through unknown arguments to the app
                EXTRA_ARGS+=("$arg")
            fi
            ;;
    esac
done

# Use custom file if specified, converting relative paths to absolute
if [[ -n "$CUSTOM_FILE" ]]; then
    if [[ "$CUSTOM_FILE" = /* ]]; then
        TEST_FILE="$CUSTOM_FILE"
    else
        TEST_FILE="$PROJECT_ROOT/$CUSTOM_FILE"
    fi
fi

RID="osx-arm64"
APP_PATH="$SCRIPT_DIR/bin/$CONFIG/net10.0-macos/$RID/JPEG XL Viewer.app"
EXE_PATH="$APP_PATH/Contents/MacOS/JpegXL.MacOS"

pkill -f "JpegXL.MacOS" 2>/dev/null || true
dotnet build -c "$CONFIG" -r "$RID" "$SCRIPT_DIR/JpegXL.MacOS.csproj" -v q
codesign --force --deep --sign - "$APP_PATH"

# Setup logging if requested
if [[ -n "$LOG_FILE" ]]; then
    mkdir -p "$SCRIPT_DIR/logs"
    if [[ -f "$LOG_FILE" ]]; then
        mv "$LOG_FILE" "$SCRIPT_DIR/logs/run-$(date +%s).txt"
    fi
fi

# Run the app
if [[ -n "$KILL_AFTER" ]]; then
    if [[ -n "$LOG_FILE" ]]; then
        "$EXE_PATH" "$TEST_FILE" "${EXTRA_ARGS[@]}" 2>&1 | tee "$LOG_FILE" &
        TEE_PID=$!
        APP_PID=$(pgrep -n "JpegXL.MacOS")
        sleep "$KILL_AFTER"
        kill $APP_PID 2>/dev/null || true
        kill $TEE_PID 2>/dev/null || true
    else
        "$EXE_PATH" "$TEST_FILE" "${EXTRA_ARGS[@]}" &
        APP_PID=$!
        sleep "$KILL_AFTER"
        kill $APP_PID 2>/dev/null || true
    fi
elif [[ -n "$LOG_FILE" ]]; then
    "$EXE_PATH" "$TEST_FILE" "${EXTRA_ARGS[@]}" 2>&1 | tee "$LOG_FILE"
else
    "$EXE_PATH" "$TEST_FILE" "${EXTRA_ARGS[@]}"
fi