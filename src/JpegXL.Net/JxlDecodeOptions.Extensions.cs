// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Extension methods for <see cref="JxlDecodeOptions"/>.
/// </summary>
public partial struct JxlDecodeOptions
{
    /// <summary>
    /// Gets a default options instance.
    /// </summary>
    /// <remarks>
    /// Default values:
    /// <list type="bullet">
    /// <item><description>AdjustOrientation: true</description></item>
    /// <item><description>RenderSpotColors: true</description></item>
    /// <item><description>Coalescing: true</description></item>
    /// <item><description>SkipPreview: true</description></item>
    /// <item><description>EnableOutput: true</description></item>
    /// <item><description>ProgressiveMode: Pass</description></item>
    /// <item><description>PixelLimit: 0 (no limit)</description></item>
    /// <item><description>DesiredIntensityTarget: 0 (use image native)</description></item>
    /// <item><description>HighPrecision: false</description></item>
    /// <item><description>PremultiplyAlpha: false</description></item>
    /// <item><description>DecodeExtraChannels: false</description></item>
    /// <item><description>PixelFormat: RGBA8 (default)</description></item>
    /// </list>
    /// </remarks>
    public static JxlDecodeOptions Default => new()
    {
        PixelLimit = UIntPtr.Zero,
        DesiredIntensityTarget = 0f,
        ProgressiveMode = JxlProgressiveMode.Pass,
        AdjustOrientation = true,
        RenderSpotColors = true,
        Coalescing = true,
        SkipPreview = true,
        EnableOutput = true,
        HighPrecision = false,
        PremultiplyAlpha = false,
        DecodeExtraChannels = false,
        PixelFormat = JxlPixelFormat.Default,
    };
}
