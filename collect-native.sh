#!/bin/bash
# Script to collect native libraries from build output into the runtimes folder structure
# Run this after building native libraries for all platforms (typically in CI)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NATIVE_ROOT="$SCRIPT_DIR/native/jxlrs/target"
RUNTIMES_ROOT="$SCRIPT_DIR/src/JpegXL.Net/runtimes"

echo "Collecting native libraries into runtimes folders..."

copy_if_exists() {
    local rust_target="$1"
    local rid="$2"
    local libname="$3"
    
    local src_path="$NATIVE_ROOT/$rust_target/release/$libname"
    local dst_dir="$RUNTIMES_ROOT/$rid/native"
    local dst_path="$dst_dir/$libname"
    
    if [ -f "$src_path" ]; then
        mkdir -p "$dst_dir"
        cp "$src_path" "$dst_path"
        echo "  ✓ $rust_target -> $rid ($libname)"
    else
        echo "  ✗ $rust_target - not found"
    fi
}

# Windows
copy_if_exists "x86_64-pc-windows-msvc" "win-x64" "jxlrs.dll"
copy_if_exists "aarch64-pc-windows-msvc" "win-arm64" "jxlrs.dll"

# Linux
copy_if_exists "x86_64-unknown-linux-gnu" "linux-x64" "libjxlrs.so"
copy_if_exists "aarch64-unknown-linux-gnu" "linux-arm64" "libjxlrs.so"

# macOS
copy_if_exists "x86_64-apple-darwin" "osx-x64" "libjxlrs.dylib"
copy_if_exists "aarch64-apple-darwin" "osx-arm64" "libjxlrs.dylib"

echo ""
echo "Done. Native libraries collected:"
find "$RUNTIMES_ROOT" -type f \( -name "*.dll" -o -name "*.so" -o -name "*.dylib" \) -exec ls -lh {} \;
