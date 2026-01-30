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

## How Animation Playback Works

Animated JPEG XL files are displayed using a GPU-optimized architecture that pre-loads all frames into video memory for smooth playback.

### Loading Process

A two-pass approach minimizes memory usage:

1. **Metadata Pass**: The decoder reads frame headers using `SkipFrame()` to collect durations without allocating pixel buffers
2. **GPU Upload Pass**: A single reusable shared buffer decodes each frame sequentially, uploading to a Metal texture array. The buffer is freed after all frames are loaded.
3. **Zero-Copy Decoding**: On Apple Silicon, unified memory means frames go directly from the decoder to GPU-accessible memory with no intermediate copies

### Frame Storage

All frames are stored in a single **Metal 2D Texture Array** (`MTLTexture2DArray`):
- Each layer of the array holds one animation frame
- Frame durations are stored separately in a CPU-side list
- This allows instant frame switching without any per-frame memory operations

### Playback

- A 60fps `NSTimer` polls elapsed time against the current frame's duration
- When elapsed time exceeds the frame duration, the next frame is displayed and the timer resets
- The current frame index is passed to the GPU as a uniform buffer
- The fragment shader samples from the texture array at the specified layer index

### Performance Characteristics

| Aspect | Approach |
|--------|----------|
| Memory | GPU-resident texture array; reusable decode buffer freed after loading |
| Decode | Single decode per frame at load time; no runtime decoding |
| Render | Single draw call; GPU selects frame via shader |
| Timing | 60fps timer with elapsed-time comparison for frame advancement |

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
