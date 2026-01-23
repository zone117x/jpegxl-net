# JpegXL.MacOS - Native HDR Viewer

A native macOS JPEG XL viewer with **true HDR display support** using Metal and Extended Dynamic Range (EDR).

## Features

- **True HDR display** via macOS EDR (Extended Dynamic Range)
- Metal-accelerated rendering with Float16 pixel format
- Display P3 wide color gamut support
- Animation playback for animated JPEG XL files
- Drag and drop support
- Zoom controls
- Shows EDR headroom of your display

## Requirements

- macOS 14.0 or later
- .NET 10.0 SDK
- **Xcode** (full installation from App Store, not just Command Line Tools)
- macOS workload for .NET

## Setup

### 1. Install Xcode

Install Xcode from the Mac App Store, then set it as the active developer directory:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

### 2. Install the macOS workload

```bash
sudo dotnet workload install macos
```

### 3. Build the project

```bash
cd examples/JpegXL.MacOS
dotnet build
```

### 4. Run the app

```bash
dotnet run
```

Or to open a specific file:

```bash
dotnet run -- /path/to/image.jxl
```

## How HDR Works

This viewer uses Apple's **EDR (Extended Dynamic Range)** technology:

1. **CAMetalLayer** is configured with:
   - `WantsExtendedDynamicRangeContent = true`
   - `PixelFormat = RGBA16Float`
   - `ColorSpace = ExtendedLinearDisplayP3`

2. **HDR images** are decoded to Float32 RGBA format, preserving the full dynamic range

3. **The display system** handles tone mapping based on:
   - Display capabilities (HDR vs SDR)
   - Ambient light conditions
   - System brightness settings

4. **EDR headroom** is queried from `NSScreen.MaximumExtendedDynamicRangeColorComponentValue`
   - Value of 1.0 = SDR only
   - Value > 1.0 = HDR capable (e.g., 2.0 means 2x SDR white)

## Comparison with Avalonia Viewer

| Feature | JpegXL.Viewer (Avalonia) | JpegXL.MacOS |
|---------|--------------------------|--------------|
| Platform | Cross-platform | macOS only |
| HDR Display | Tone-mapped to SDR | True HDR via EDR |
| Rendering | Skia/8-bit | Metal/Float16 |
| Color Space | sRGB | Display P3 (extended) |

## Troubleshooting

### "A valid Xcode installation was not found"

Install Xcode from the App Store, then run:
```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

### "workloads must be installed: macos"

Run: `sudo dotnet workload install macos`

### No HDR effect visible

- Check that your display supports HDR/EDR
- The status bar shows "EDR: X.Xx" if your display supports extended range
- Lower display brightness increases EDR headroom on some displays
- True HDR monitors (like Pro Display XDR) will show the full effect
