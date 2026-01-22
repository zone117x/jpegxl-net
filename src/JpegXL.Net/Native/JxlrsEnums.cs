// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net.Native;

/// <summary>
/// Pixel data format.
/// </summary>
public enum JxlrsDataFormat
{
    /// <summary>
    /// 8-bit unsigned integer per channel.
    /// </summary>
    Uint8 = 0,

    /// <summary>
    /// 16-bit unsigned integer per channel.
    /// </summary>
    Uint16 = 1,

    /// <summary>
    /// 16-bit float per channel.
    /// </summary>
    Float16 = 2,

    /// <summary>
    /// 32-bit float per channel.
    /// </summary>
    Float32 = 3,
}

/// <summary>
/// Color channel layout.
/// </summary>
public enum JxlrsColorType
{
    /// <summary>
    /// Single grayscale channel.
    /// </summary>
    Grayscale = 0,

    /// <summary>
    /// Grayscale + alpha.
    /// </summary>
    GrayscaleAlpha = 1,

    /// <summary>
    /// Red, green, blue.
    /// </summary>
    Rgb = 2,

    /// <summary>
    /// Red, green, blue, alpha.
    /// </summary>
    Rgba = 3,

    /// <summary>
    /// Blue, green, red (Windows bitmap order).
    /// </summary>
    Bgr = 4,

    /// <summary>
    /// Blue, green, red, alpha.
    /// </summary>
    Bgra = 5,
}

/// <summary>
/// Endianness for multi-byte pixel formats.
/// </summary>
public enum JxlrsEndianness
{
    /// <summary>
    /// Use native endianness of the platform.
    /// </summary>
    Native = 0,

    /// <summary>
    /// Little endian byte order.
    /// </summary>
    LittleEndian = 1,

    /// <summary>
    /// Big endian byte order.
    /// </summary>
    BigEndian = 2,
}

/// <summary>
/// Image orientation (EXIF-style).
/// </summary>
public enum JxlrsOrientation
{
    /// <summary>
    /// Normal orientation.
    /// </summary>
    Identity = 1,

    /// <summary>
    /// Flipped horizontally.
    /// </summary>
    FlipHorizontal = 2,

    /// <summary>
    /// Rotated 180 degrees.
    /// </summary>
    Rotate180 = 3,

    /// <summary>
    /// Flipped vertically.
    /// </summary>
    FlipVertical = 4,

    /// <summary>
    /// Transposed (swap x/y) then flipped horizontally.
    /// </summary>
    Transpose = 5,

    /// <summary>
    /// Rotated 90 degrees clockwise.
    /// </summary>
    Rotate90Cw = 6,

    /// <summary>
    /// Transposed then flipped vertically.
    /// </summary>
    AntiTranspose = 7,

    /// <summary>
    /// Rotated 90 degrees counter-clockwise.
    /// </summary>
    Rotate90Ccw = 8,
}

/// <summary>
/// Extra channel type.
/// </summary>
public enum JxlrsExtraChannelType
{
    /// <summary>
    /// Alpha/transparency channel.
    /// </summary>
    Alpha = 0,

    /// <summary>
    /// Depth map.
    /// </summary>
    Depth = 1,

    /// <summary>
    /// Spot color.
    /// </summary>
    SpotColor = 2,

    /// <summary>
    /// Selection mask.
    /// </summary>
    SelectionMask = 3,

    /// <summary>
    /// CFA (color filter array) for raw sensor data.
    /// </summary>
    Cfa = 4,

    /// <summary>
    /// Thermal data.
    /// </summary>
    Thermal = 5,

    /// <summary>
    /// Non-optional extra channel.
    /// </summary>
    NonOptional = 6,

    /// <summary>
    /// Optional extra channel.
    /// </summary>
    Optional = 7,

    /// <summary>
    /// Unknown channel type.
    /// </summary>
    Unknown = 255,
}

/// <summary>
/// Signature check result.
/// </summary>
public enum JxlrsSignature
{
    /// <summary>
    /// Not enough data to determine.
    /// </summary>
    NotEnoughBytes = 0,

    /// <summary>
    /// Not a JPEG XL file.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// Valid JPEG XL codestream.
    /// </summary>
    Codestream = 2,

    /// <summary>
    /// Valid JPEG XL container.
    /// </summary>
    Container = 3,
}
