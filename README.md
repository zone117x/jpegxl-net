# JpegXL.Net

A .NET library for decoding JPEG XL images. Wraps the [jxl-rs](https://github.com/libjxl/jxl-rs) Rust decoder with native libraries for Windows, Linux, and macOS.

## Features

- Decode JPEG XL images to raw pixel data
- Support for multiple pixel formats (RGBA, BGRA, RGB, Grayscale, etc.)
- Support for 8-bit, 16-bit, and floating-point output
- Cross-platform: Windows (x64, ARM64), Linux (x64, ARM64), macOS (x64, ARM64)
- Targets both .NET Standard 2.0 and .NET 8.0+

## Installation

```bash
dotnet add package JpegXL.Net
```

## Usage

### Simple Decoding

```csharp
using JpegXL.Net;

// Decode a JXL file to RGBA pixels
byte[] jxlData = File.ReadAllBytes("image.jxl");
using var image = JxlImage.Decode(jxlData);

Console.WriteLine($"Size: {image.Width}x{image.Height}");
Console.WriteLine($"Channels: {image.NumChannels}");

// Access pixel data
ReadOnlySpan<byte> pixels = image.Pixels;
```

### Specifying Pixel Format

```csharp
using JpegXL.Net;
using JpegXL.Net.Native;

// Decode to BGRA format (useful for Windows bitmaps)
var format = JxlrsPixelFormat.Bgra8;
using var image = JxlImage.Decode(jxlData, format);

// Decode to 16-bit RGB
var format16 = new JxlrsPixelFormat
{
    DataFormat = JxlrsDataFormat.Uint16,
    ColorType = JxlrsColorType.Rgb,
    Endianness = JxlrsEndianness.Native
};
using var image16 = JxlImage.Decode(jxlData, format16);
```

### Low-Level Decoder API

```csharp
using JpegXL.Net;
using JpegXL.Net.Native;

using var decoder = new JxlDecoder();

// Set input data
decoder.SetInput(jxlData);

// Read image info
var info = decoder.ReadInfo();
Console.WriteLine($"Image: {info.Width}x{info.Height}");
Console.WriteLine($"Bits per sample: {info.BitsPerSample}");
Console.WriteLine($"Has alpha: {info.HasAlpha}");

// Get pixel data
byte[] pixels = decoder.GetPixels();
```

### Checking File Signatures

```csharp
using JpegXL.Net;

byte[] data = File.ReadAllBytes("unknown.bin");

// Check if data is a JXL file
if (JxlImage.IsJxl(data))
{
    Console.WriteLine("This is a JPEG XL file!");
}

// Get detailed signature info
var signature = JxlImage.CheckSignature(data);
switch (signature)
{
    case JxlrsSignature.Codestream:
        Console.WriteLine("JXL codestream");
        break;
    case JxlrsSignature.Container:
        Console.WriteLine("JXL container (ISOBMFF)");
        break;
    case JxlrsSignature.Invalid:
        Console.WriteLine("Not a JXL file");
        break;
}
```

## Supported Platforms

| Platform | Architecture | Status |
|----------|-------------|--------|
| Windows  | x64         | ✅     |
| Windows  | ARM64       | ✅     |
| Linux    | x64         | ✅     |
| Linux    | ARM64       | ✅     |
| macOS    | x64         | ✅     |
| macOS    | ARM64       | ✅     |

## Architecture

JpegXL.Net wraps the Rust-based jxl-rs decoder through multiple layers:

```
┌─────────────────────────────────────────────────┐
│                  Your Application               │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│               JpegXL.Net (C# API)               │
│  • JxlImage - one-shot high-level API           │
│  • JxlDecoder - streaming low-level API         │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│       JpegXL.Net.Generators (Source Gen)        │
│  • Generates public PascalCase wrapper types    │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│         NativeMethods.g.cs (P/Invoke)           │
│  • Auto-generated C# bindings from Rust FFI     │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│           jxl-ffi (Rust FFI Wrapper)            │
│  • extern "C" functions for .NET interop        │
│  • csbindgen generates C# bindings at build     │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│           jxl-rs (3rd Party Decoder)            │
│  • High-performance Rust JPEG XL decoder        │
│  • SIMD support (SSE4.2, AVX, AVX-512, NEON)    │
└─────────────────────────────────────────────────┘
```

### jxl-rs (Third-Party Decoder)

The [jxl-rs](https://github.com/libjxl/jxl-rs) library is a pure Rust implementation of the JPEG XL decoder, included as a Git submodule at `jxl-rs/`. It provides a streaming decoding API with SIMD-accelerated transforms for high performance. Licensed under BSD-3-Clause.

### jxl-ffi (Native FFI Layer)

Located at `native/jxl-ffi/`, this Rust crate wraps jxl-rs with `extern "C"` functions suitable for cross-language interop. The build process uses [csbindgen](https://github.com/Cysharp/csbindgen) to automatically generate C# P/Invoke bindings from the Rust function signatures.

- **Source**: `native/jxl-ffi/src/` (decoder.rs, types.rs, error.rs)
- **Build script**: `native/jxl-ffi/build.rs` runs csbindgen
- **Generated bindings**: `src/JpegXL.Net/Native/NativeMethods.g.cs`
- **Output libraries**: `jxl_ffi.dll` (Windows), `libjxl_ffi.so` (Linux), `libjxl_ffi.dylib` (macOS)

### JpegXL.Net.Generators (Source Generator)

Located at `src/JpegXL.Net.Generators/`, this Roslyn incremental source generator transforms the internal snake_case FFI types into idiomatic public C# types with PascalCase naming. It handles conversions like `have_alpha` → `bool HasAlpha` and wraps native structs in safe readonly struct wrappers.

### JpegXL.Net (Public API)

Located at `src/JpegXL.Net/`, this is the main library distributed via NuGet. It provides:

- **JxlImage** - Simple one-shot decode API for loading entire images
- **JxlDecoder** - Streaming event-driven API for advanced use cases (animation, progressive decoding)
- **JxlDecodeOptions** - Configuration options (HDR intensity target, alpha handling, progressive mode)
- **Cross-platform support** - Automatic native library resolution for all supported platforms

## Example Applications

### JpegXL.Viewer (Cross-Platform)

Located at `examples/JpegXL.Viewer/`, this Avalonia-based viewer runs on Windows, macOS, and Linux. Features include animation playback and HDR content display with tone mapping to SDR.

### JpegXL.MacOS (Native HDR)

Located at `examples/JpegXL.MacOS/`, this native macOS application demonstrates full HDR display support:

- **Metal rendering** with RGBA16Float textures
- **Extended Dynamic Range (EDR)** for HDR displays
- **Float32 decoding** without tone mapping for accurate HDR reproduction
- **Display P3 linear** color space support

## License

This project is licensed under the MIT License.

The underlying jxl-rs library is licensed under the BSD-3-Clause license.
