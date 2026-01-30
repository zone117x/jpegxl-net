#!/usr/bin/env bash
# Build native libraries for all supported platforms
# Usage: ./build-native.sh [--release] [--target TARGET]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Configuration
BUILD_MODE="release"
SINGLE_TARGET=""
OUTPUT_DIR="$SCRIPT_DIR/artifacts"

# Get Rust target for RID
get_rust_target() {
    case "$1" in
        win-x64) echo "x86_64-pc-windows-gnu" ;;
        win-arm64) echo "aarch64-pc-windows-gnullvm" ;;
        linux-x64) echo "x86_64-unknown-linux-gnu" ;;
        linux-arm64) echo "aarch64-unknown-linux-gnu" ;;
        osx-x64) echo "x86_64-apple-darwin" ;;
        osx-arm64) echo "aarch64-apple-darwin" ;;
        *) echo "" ;;
    esac
}

# Get library name for RID
get_lib_name() {
    case "$1" in
        win-*) echo "jxl_ffi.dll" ;;
        linux-*) echo "libjxl_ffi.so" ;;
        osx-*) echo "libjxl_ffi.dylib" ;;
        *) echo "" ;;
    esac
}

# All supported RIDs
ALL_RIDS="win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --debug)
            BUILD_MODE="debug"
            shift
            ;;
        --release)
            BUILD_MODE="release"
            shift
            ;;
        --target)
            SINGLE_TARGET="$2"
            shift 2
            ;;
        --output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --debug         Build in debug mode"
            echo "  --release       Build in release mode (default)"
            echo "  --target RID    Build only for specific RID (win-x64, linux-arm64, etc.)"
            echo "  --output DIR    Output directory for artifacts"
            echo ""
            echo "Supported targets: $ALL_RIDS"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Determine build flags
if [[ "$BUILD_MODE" == "release" ]]; then
    CARGO_FLAGS="--release"
    TARGET_SUBDIR="release"
else
    CARGO_FLAGS=""
    TARGET_SUBDIR="debug"
fi

# Detect current platform
detect_host_target() {
    local arch=$(uname -m)
    local os=$(uname -s)
    
    case "$os" in
        Darwin)
            case "$arch" in
                x86_64) echo "osx-x64" ;;
                arm64) echo "osx-arm64" ;;
            esac
            ;;
        Linux)
            case "$arch" in
                x86_64) echo "linux-x64" ;;
                aarch64) echo "linux-arm64" ;;
            esac
            ;;
        MINGW*|MSYS*|CYGWIN*)
            case "$arch" in
                x86_64) echo "win-x64" ;;
                aarch64) echo "win-arm64" ;;
            esac
            ;;
    esac
}

HOST_RID=$(detect_host_target)
echo "Host platform: $HOST_RID"

# Function to build for a target
build_target() {
    local rid="$1"
    local rust_target
    rust_target=$(get_rust_target "$rid")
    local lib_name
    lib_name=$(get_lib_name "$rid")
    
    if [[ -z "$rust_target" ]]; then
        echo "Unknown RID: $rid"
        return 1
    fi
    
    echo ""
    echo "=========================================="
    echo "Building for $rid ($rust_target)"
    echo "=========================================="
    
    local output_subdir="$OUTPUT_DIR/runtimes/$rid/native"
    mkdir -p "$output_subdir"
    
    # Check if this is a cross-compilation
    if [[ "$rid" == "$HOST_RID" ]]; then
        # Native build
        echo "Native build..."
        cargo build $CARGO_FLAGS --target "$rust_target"
    elif [[ "$rid" == osx-* && "$HOST_RID" == osx-* ]]; then
        # macOS cross-compile (x64 <-> arm64) - native toolchain works
        echo "macOS cross-architecture build..."
        rustup target add "$rust_target" 2>/dev/null || true
        cargo build $CARGO_FLAGS --target "$rust_target"
    else
        # Cross-compilation using 'cross'
        echo "Cross-compiling with 'cross'..."
        if ! command -v cross &> /dev/null; then
            echo "Installing 'cross' for cross-compilation..."
            cargo install cross --git https://github.com/cross-rs/cross
        fi
        cross build $CARGO_FLAGS --target "$rust_target"
    fi
    
    # Copy artifact
    local src_path="target/$rust_target/$TARGET_SUBDIR/$lib_name"
    if [[ -f "$src_path" ]]; then
        cp "$src_path" "$output_subdir/"
        echo "✓ Copied $lib_name to $output_subdir/"
    else
        echo "✗ Build artifact not found: $src_path"
        return 1
    fi
}

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Build targets
if [[ -n "$SINGLE_TARGET" ]]; then
    rust_target=$(get_rust_target "$SINGLE_TARGET")
    if [[ -z "$rust_target" ]]; then
        echo "Unknown target: $SINGLE_TARGET"
        echo "Valid targets: $ALL_RIDS"
        exit 1
    fi
    build_target "$SINGLE_TARGET"
else
    # Build all targets
    for rid in $ALL_RIDS; do
        build_target "$rid" || echo "Warning: Failed to build for $rid"
    done
fi

# Copy header file
echo ""
echo "Copying C header..."
mkdir -p "$OUTPUT_DIR/include"
cp include/jxl_ffi.h "$OUTPUT_DIR/include/"

echo ""
echo "=========================================="
echo "Build complete!"
echo "Artifacts in: $OUTPUT_DIR"
echo "=========================================="
ls -la "$OUTPUT_DIR/runtimes/"*/native/ 2>/dev/null || true
