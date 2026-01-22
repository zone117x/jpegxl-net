# JPEG XL Viewer

A simple cross-platform image viewer for JPEG XL files, built with [Avalonia UI](https://avaloniaui.net/).

## Features

- Open JPEG XL (.jxl) files via file picker or drag-and-drop
- Zoom in/out and reset zoom
- Displays image metadata (dimensions, bit depth, alpha channel)
- Works on Windows, macOS, and Linux

## Running

```bash
cd examples/JpegXL.Viewer
dotnet run
```

Or from the repository root:

```bash
dotnet run --project examples/JpegXL.Viewer/JpegXL.Viewer.csproj
```

## Building

```bash
dotnet build examples/JpegXL.Viewer/JpegXL.Viewer.csproj
```

## Screenshot

![JPEG XL Viewer](screenshot.png)

## Dependencies

- [Avalonia UI 11.x](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [JpegXL.Net](../../src/JpegXL.Net/) - JPEG XL decoder library
