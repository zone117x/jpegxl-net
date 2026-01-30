// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Extension properties for JxlPixelFormat.
/// </summary>
public partial struct JxlPixelFormat
{
    /// <summary>
    /// Gets the default pixel format (RGBA, 8-bit, native endianness).
    /// </summary>
    public static JxlPixelFormat Default => new()
    {
        DataFormat = JxlDataFormat.Uint8,
        ColorType = JxlColorType.Rgba,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets an RGBA 8-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Rgba8 => new()
    {
        DataFormat = JxlDataFormat.Uint8,
        ColorType = JxlColorType.Rgba,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets a BGRA 8-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Bgra8 => new()
    {
        DataFormat = JxlDataFormat.Uint8,
        ColorType = JxlColorType.Bgra,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets an RGB 8-bit pixel format (no alpha).
    /// </summary>
    public static JxlPixelFormat Rgb8 => new()
    {
        DataFormat = JxlDataFormat.Uint8,
        ColorType = JxlColorType.Rgb,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets an RGBA 16-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Rgba16 => new()
    {
        DataFormat = JxlDataFormat.Uint16,
        ColorType = JxlColorType.Rgba,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets an RGBA 32-bit float pixel format (HDR).
    /// </summary>
    public static JxlPixelFormat Rgba32F => new()
    {
        DataFormat = JxlDataFormat.Float32,
        ColorType = JxlColorType.Rgba,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };

    /// <summary>
    /// Gets a BGRA 32-bit float pixel format (HDR).
    /// </summary>
    public static JxlPixelFormat Bgra32F => new()
    {
        DataFormat = JxlDataFormat.Float32,
        ColorType = JxlColorType.Bgra,
        Endianness = global::JpegXL.Net.JxlEndianness.Native
    };
}
