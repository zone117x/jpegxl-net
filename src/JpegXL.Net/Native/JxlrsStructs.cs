// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System.Runtime.InteropServices;

namespace JpegXL.Net.Native;

/// <summary>
/// Pixel format specification.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JxlrsPixelFormat
{
    /// <summary>
    /// Data format for each channel.
    /// </summary>
    public JxlrsDataFormat DataFormat;

    /// <summary>
    /// Color channel layout.
    /// </summary>
    public JxlrsColorType ColorType;

    /// <summary>
    /// Endianness for formats > 8 bits.
    /// </summary>
    public JxlrsEndianness Endianness;

    /// <summary>
    /// Creates a default RGBA 8-bit pixel format.
    /// </summary>
    public static JxlrsPixelFormat Default => new()
    {
        DataFormat = JxlrsDataFormat.Uint8,
        ColorType = JxlrsColorType.Rgba,
        Endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates a BGRA 8-bit pixel format (Windows bitmap order).
    /// </summary>
    public static JxlrsPixelFormat Bgra8 => new()
    {
        DataFormat = JxlrsDataFormat.Uint8,
        ColorType = JxlrsColorType.Bgra,
        Endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGB 8-bit pixel format (no alpha).
    /// </summary>
    public static JxlrsPixelFormat Rgb8 => new()
    {
        DataFormat = JxlrsDataFormat.Uint8,
        ColorType = JxlrsColorType.Rgb,
        Endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGBA 16-bit pixel format.
    /// </summary>
    public static JxlrsPixelFormat Rgba16 => new()
    {
        DataFormat = JxlrsDataFormat.Uint16,
        ColorType = JxlrsColorType.Rgba,
        Endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGBA 32-bit float pixel format.
    /// </summary>
    public static JxlrsPixelFormat RgbaFloat => new()
    {
        DataFormat = JxlrsDataFormat.Float32,
        ColorType = JxlrsColorType.Rgba,
        Endianness = JxlrsEndianness.Native,
    };
}

/// <summary>
/// Basic image information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JxlrsBasicInfo
{
    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public uint Width;

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public uint Height;

    /// <summary>
    /// Bits per sample for integer formats.
    /// </summary>
    public uint BitsPerSample;

    /// <summary>
    /// Exponent bits (0 for integer formats, >0 for float).
    /// </summary>
    public uint ExponentBitsPerSample;

    /// <summary>
    /// Number of color channels (1 for grayscale, 3 for RGB).
    /// </summary>
    public uint NumColorChannels;

    /// <summary>
    /// Number of extra channels (alpha, depth, etc.).
    /// </summary>
    public uint NumExtraChannels;

    /// <summary>
    /// Whether alpha is premultiplied (0 = no, 1 = yes).
    /// </summary>
    public int AlphaPremultiplied;

    /// <summary>
    /// Image orientation.
    /// </summary>
    public JxlrsOrientation Orientation;

    /// <summary>
    /// Whether the image has animation (0 = no, 1 = yes).
    /// </summary>
    public int HaveAnimation;

    /// <summary>
    /// Animation ticks per second numerator (0 if no animation).
    /// </summary>
    public uint AnimationTpsNumerator;

    /// <summary>
    /// Animation ticks per second denominator (0 if no animation).
    /// </summary>
    public uint AnimationTpsDenominator;

    /// <summary>
    /// Number of animation loops (0 = infinite).
    /// </summary>
    public uint AnimationNumLoops;

    /// <summary>
    /// Whether original color profile is used (0 = no, 1 = yes).
    /// </summary>
    public int UsesOriginalProfile;

    /// <summary>
    /// Preview image width (0 if no preview).
    /// </summary>
    public uint PreviewWidth;

    /// <summary>
    /// Preview image height (0 if no preview).
    /// </summary>
    public uint PreviewHeight;

    /// <summary>
    /// Intensity target for HDR (nits).
    /// </summary>
    public float IntensityTarget;

    /// <summary>
    /// Minimum nits for tone mapping.
    /// </summary>
    public float MinNits;

    /// <summary>
    /// Gets whether the image has an alpha channel.
    /// </summary>
    public readonly bool HasAlpha => NumExtraChannels > 0;

    /// <summary>
    /// Gets whether the image is an animation.
    /// </summary>
    public readonly bool IsAnimated => HaveAnimation != 0;

    /// <summary>
    /// Gets the animation frame rate in frames per second.
    /// </summary>
    public readonly double FrameRate =>
        AnimationTpsDenominator > 0
            ? (double)AnimationTpsNumerator / AnimationTpsDenominator
            : 0;
}

/// <summary>
/// Information about an extra channel.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct JxlrsExtraChannelInfo
{
    /// <summary>
    /// Type of extra channel.
    /// </summary>
    public JxlrsExtraChannelType ChannelType;

    /// <summary>
    /// Bits per sample.
    /// </summary>
    public uint BitsPerSample;

    /// <summary>
    /// Exponent bits (for float channels).
    /// </summary>
    public uint ExponentBitsPerSample;

    /// <summary>
    /// Whether alpha is premultiplied (only for alpha channels).
    /// </summary>
    public int AlphaPremultiplied;

    /// <summary>
    /// Spot color values (RGBA, only for spot color channels).
    /// </summary>
    public fixed float SpotColor[4];

    /// <summary>
    /// Channel name length in bytes (excluding null terminator).
    /// </summary>
    public uint NameLength;
}

/// <summary>
/// Frame header information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JxlrsFrameHeader
{
    /// <summary>
    /// Frame duration in animation ticks.
    /// </summary>
    public uint Duration;

    /// <summary>
    /// Timecode (for video).
    /// </summary>
    public uint Timecode;

    /// <summary>
    /// Frame name length in bytes (excluding null terminator).
    /// </summary>
    public uint NameLength;

    /// <summary>
    /// Whether this is the last frame (0 = no, 1 = yes).
    /// </summary>
    public int IsLast;
}
