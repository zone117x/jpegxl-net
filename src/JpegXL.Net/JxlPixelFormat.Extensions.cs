// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using NativeFormat = JpegXL.Net.Native.JxlPixelFormat;
using NativeDataFormat = JpegXL.Net.Native.JxlDataFormat;
using NativeColorType = JpegXL.Net.Native.JxlColorType;
using NativeEndianness = JpegXL.Net.Native.JxlEndianness;

namespace JpegXL.Net;

/// <summary>
/// Extension properties for JxlPixelFormat.
/// </summary>
public partial struct JxlPixelFormat
{
    /// <summary>
    /// Gets the default pixel format (RGBA, 8-bit, native endianness).
    /// </summary>
    public static JxlPixelFormat Default => new(new NativeFormat
    {
        data_format = NativeDataFormat.Uint8,
        color_type = NativeColorType.Rgba,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets an RGBA 8-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Rgba8 => new(new NativeFormat
    {
        data_format = NativeDataFormat.Uint8,
        color_type = NativeColorType.Rgba,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets a BGRA 8-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Bgra8 => new(new NativeFormat
    {
        data_format = NativeDataFormat.Uint8,
        color_type = NativeColorType.Bgra,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets an RGB 8-bit pixel format (no alpha).
    /// </summary>
    public static JxlPixelFormat Rgb8 => new(new NativeFormat
    {
        data_format = NativeDataFormat.Uint8,
        color_type = NativeColorType.Rgb,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets an RGBA 16-bit pixel format.
    /// </summary>
    public static JxlPixelFormat Rgba16 => new(new NativeFormat
    {
        data_format = NativeDataFormat.Uint16,
        color_type = NativeColorType.Rgba,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets an RGBA 32-bit float pixel format (HDR).
    /// </summary>
    public static JxlPixelFormat Rgba32F => new(new NativeFormat
    {
        data_format = NativeDataFormat.Float32,
        color_type = NativeColorType.Rgba,
        endianness = NativeEndianness.Native
    });

    /// <summary>
    /// Gets a BGRA 32-bit float pixel format (HDR).
    /// </summary>
    public static JxlPixelFormat Bgra32F => new(new NativeFormat
    {
        data_format = NativeDataFormat.Float32,
        color_type = NativeColorType.Bgra,
        endianness = NativeEndianness.Native
    });
}
