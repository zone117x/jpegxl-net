#!/bin/bash
set -e

# Full rebuild script for jpegxl-net
# Cleans all build outputs and rebuilds native + .NET projects

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "======================================"
echo "  jpegxl-net Full Rebuild"
echo "======================================"

# Detect platform and architecture
OS=$(uname -s)
ARCH=$(uname -m)

case "$OS" in
    Darwin)
        case "$ARCH" in
            arm64) RUST_TARGET="aarch64-apple-darwin" ;;
            x86_64) RUST_TARGET="x86_64-apple-darwin" ;;
        esac
        NATIVE_LIB="libjxlrs.dylib"
        ;;
    Linux)
        case "$ARCH" in
            aarch64) RUST_TARGET="aarch64-unknown-linux-gnu" ;;
            x86_64) RUST_TARGET="x86_64-unknown-linux-gnu" ;;
        esac
        NATIVE_LIB="libjxlrs.so"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        case "$ARCH" in
            aarch64) RUST_TARGET="aarch64-pc-windows-msvc" ;;
            *) RUST_TARGET="x86_64-pc-windows-msvc" ;;
        esac
        NATIVE_LIB="jxlrs.dll"
        ;;
    *)
        echo "Unsupported OS: $OS"
        exit 1
        ;;
esac

echo "Platform: $OS ($ARCH)"
echo "Rust target: $RUST_TARGET"
echo "Native lib: $NATIVE_LIB"
echo ""

# Step 1: Clean all .NET build outputs
echo "Step 1: Cleaning .NET build outputs..."
find . -type d -name "bin" -not -path "./native/*" -exec rm -rf {} + 2>/dev/null || true
find . -type d -name "obj" -not -path "./native/*" -exec rm -rf {} + 2>/dev/null || true
echo "  ✓ Cleaned bin/obj directories"

# Step 2: Clean native library from runtimes folders
echo ""
echo "Step 2: Cleaning native libraries from output locations..."
find . -name "$NATIVE_LIB" -not -path "./native/*" -delete 2>/dev/null || true
echo "  ✓ Cleaned stale native libraries"

# Step 3: Build native Rust library
echo ""
echo "Step 3: Building native Rust library..."
cd native/jxlrs

# Build for the specific target (required for Directory.Build.targets)
echo "  Building for $RUST_TARGET..."
cargo build --release --target "$RUST_TARGET"

# Also build without target for simple `cargo build` workflow
echo "  Building default target..."
cargo build --release

cd "$SCRIPT_DIR"
echo "  ✓ Native library built"

# Step 4: Copy to runtimes folder for packaging
echo ""
echo "Step 4: Updating runtimes folder..."
case "$OS" in
    Darwin)
        case "$ARCH" in
            arm64) RID="osx-arm64" ;;
            x86_64) RID="osx-x64" ;;
        esac
        ;;
    Linux)
        case "$ARCH" in
            aarch64) RID="linux-arm64" ;;
            x86_64) RID="linux-x64" ;;
        esac
        ;;
    MINGW*|MSYS*|CYGWIN*)
        case "$ARCH" in
            aarch64) RID="win-arm64" ;;
            *) RID="win-x64" ;;
        esac
        ;;
esac

RUNTIME_DIR="src/JpegXL.Net/runtimes/$RID/native"
mkdir -p "$RUNTIME_DIR"
cp "native/jxlrs/target/$RUST_TARGET/release/$NATIVE_LIB" "$RUNTIME_DIR/"
echo "  ✓ Copied to $RUNTIME_DIR"

# Step 5: Build .NET solution
echo ""
echo "Step 5: Building .NET solution..."
dotnet build
echo "  ✓ .NET build complete"

# Step 6: Run tests
echo ""
echo "Step 6: Running tests..."
dotnet test --no-build
echo "  ✓ All tests passed"

echo ""
echo "======================================"
echo "  Full rebuild complete!"
echo "======================================"
echo ""
echo "Native library: native/jxlrs/target/$RUST_TARGET/release/$NATIVE_LIB"
echo ""
