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

## License

This project is licensed under the MIT License.

The underlying jxl-rs library is licensed under the BSD-3-Clause license.
