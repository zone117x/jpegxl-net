// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.
// This file extends the auto-generated types with C#-friendly APIs.

namespace JpegXL.Net.Native;

/// <summary>
/// Pixel format specification extensions.
/// </summary>
public unsafe partial struct JxlrsPixelFormat
{
    /// <summary>
    /// Data format for each channel.
    /// </summary>
    public JxlrsDataFormat DataFormat
    {
        readonly get => data_format;
        set => data_format = value;
    }

    /// <summary>
    /// Color channel layout.
    /// </summary>
    public JxlrsColorType ColorType
    {
        readonly get => color_type;
        set => color_type = value;
    }

    /// <summary>
    /// Endianness for formats > 8 bits.
    /// </summary>
    public JxlrsEndianness Endianness
    {
        readonly get => endianness;
        set => endianness = value;
    }

    /// <summary>
    /// Creates a default RGBA 8-bit pixel format.
    /// </summary>
    public static JxlrsPixelFormat Default => new()
    {
        data_format = JxlrsDataFormat.Uint8,
        color_type = JxlrsColorType.Rgba,
        endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates a BGRA 8-bit pixel format (Windows bitmap order).
    /// </summary>
    public static JxlrsPixelFormat Bgra8 => new()
    {
        data_format = JxlrsDataFormat.Uint8,
        color_type = JxlrsColorType.Bgra,
        endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGB 8-bit pixel format (no alpha).
    /// </summary>
    public static JxlrsPixelFormat Rgb8 => new()
    {
        data_format = JxlrsDataFormat.Uint8,
        color_type = JxlrsColorType.Rgb,
        endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGBA 16-bit pixel format.
    /// </summary>
    public static JxlrsPixelFormat Rgba16 => new()
    {
        data_format = JxlrsDataFormat.Uint16,
        color_type = JxlrsColorType.Rgba,
        endianness = JxlrsEndianness.Native,
    };

    /// <summary>
    /// Creates an RGBA 32-bit float pixel format.
    /// </summary>
    public static JxlrsPixelFormat RgbaFloat => new()
    {
        data_format = JxlrsDataFormat.Float32,
        color_type = JxlrsColorType.Rgba,
        endianness = JxlrsEndianness.Native,
    };
}

/// <summary>
/// Basic image information extensions.
/// </summary>
public unsafe partial struct JxlrsBasicInfo
{
    /// <summary>Image width in pixels.</summary>
    public uint Width
    {
        readonly get => width;
        set => width = value;
    }

    /// <summary>Image height in pixels.</summary>
    public uint Height
    {
        readonly get => height;
        set => height = value;
    }

    /// <summary>Bits per sample for integer formats.</summary>
    public uint BitsPerSample
    {
        readonly get => bits_per_sample;
        set => bits_per_sample = value;
    }

    /// <summary>Exponent bits (0 for integer formats, >0 for float).</summary>
    public uint ExponentBitsPerSample
    {
        readonly get => exponent_bits_per_sample;
        set => exponent_bits_per_sample = value;
    }

    /// <summary>Number of color channels (1 for grayscale, 3 for RGB).</summary>
    public uint NumColorChannels
    {
        readonly get => num_color_channels;
        set => num_color_channels = value;
    }

    /// <summary>Number of extra channels (alpha, depth, etc.).</summary>
    public uint NumExtraChannels
    {
        readonly get => num_extra_channels;
        set => num_extra_channels = value;
    }

    /// <summary>Whether alpha is premultiplied.</summary>
    public bool AlphaPremultiplied
    {
        readonly get => alpha_premultiplied != 0;
        set => alpha_premultiplied = value ? 1 : 0;
    }

    /// <summary>Image orientation.</summary>
    public JxlrsOrientation Orientation
    {
        readonly get => orientation;
        set => orientation = value;
    }

    /// <summary>Whether the image has animation.</summary>
    public bool HaveAnimation
    {
        readonly get => have_animation != 0;
        set => have_animation = value ? 1 : 0;
    }

    /// <summary>Animation ticks per second numerator (0 if no animation).</summary>
    public uint AnimationTpsNumerator
    {
        readonly get => animation_tps_numerator;
        set => animation_tps_numerator = value;
    }

    /// <summary>Animation ticks per second denominator (0 if no animation).</summary>
    public uint AnimationTpsDenominator
    {
        readonly get => animation_tps_denominator;
        set => animation_tps_denominator = value;
    }

    /// <summary>Number of animation loops (0 = infinite).</summary>
    public uint AnimationNumLoops
    {
        readonly get => animation_num_loops;
        set => animation_num_loops = value;
    }

    /// <summary>Whether original color profile is used.</summary>
    public bool UsesOriginalProfile
    {
        readonly get => uses_original_profile != 0;
        set => uses_original_profile = value ? 1 : 0;
    }

    /// <summary>Preview image width (0 if no preview).</summary>
    public uint PreviewWidth
    {
        readonly get => preview_width;
        set => preview_width = value;
    }

    /// <summary>Preview image height (0 if no preview).</summary>
    public uint PreviewHeight
    {
        readonly get => preview_height;
        set => preview_height = value;
    }

    /// <summary>Intensity target for HDR (nits).</summary>
    public float IntensityTarget
    {
        readonly get => intensity_target;
        set => intensity_target = value;
    }

    /// <summary>Minimum nits for tone mapping.</summary>
    public float MinNits
    {
        readonly get => min_nits;
        set => min_nits = value;
    }

    /// <summary>Gets whether the image has an alpha channel.</summary>
    public readonly bool HasAlpha => num_extra_channels > 0;

    /// <summary>Gets whether the image is an animation.</summary>
    public readonly bool IsAnimated => have_animation != 0;

    /// <summary>Gets the animation frame rate in frames per second.</summary>
    public readonly double FrameRate =>
        animation_tps_denominator > 0
            ? (double)animation_tps_numerator / animation_tps_denominator
            : 0;
}

/// <summary>
/// Extra channel information extensions.
/// </summary>
public unsafe partial struct JxlrsExtraChannelInfo
{
    /// <summary>Type of extra channel.</summary>
    public JxlrsExtraChannelType ChannelType
    {
        readonly get => channel_type;
        set => channel_type = value;
    }

    /// <summary>Bits per sample.</summary>
    public uint BitsPerSample
    {
        readonly get => bits_per_sample;
        set => bits_per_sample = value;
    }

    /// <summary>Exponent bits (for float channels).</summary>
    public uint ExponentBitsPerSample
    {
        readonly get => exponent_bits_per_sample;
        set => exponent_bits_per_sample = value;
    }

    /// <summary>Whether alpha is premultiplied (only for alpha channels).</summary>
    public bool AlphaPremultiplied
    {
        readonly get => alpha_premultiplied != 0;
        set => alpha_premultiplied = value ? 1 : 0;
    }

    /// <summary>Channel name length in bytes (excluding null terminator).</summary>
    public uint NameLength
    {
        readonly get => name_length;
        set => name_length = value;
    }
}
