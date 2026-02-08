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
    /// <item><description>ProgressiveMode: Pass</description></item>
    /// <item><description>PixelLimit: 0 (no limit)</description></item>
    /// <item><description>HighPrecision: false</description></item>
    /// <item><description>PremultiplyAlpha: false</description></item>
    /// <item><description>DecodeExtraChannels: false</description></item>
    /// <item><description>ToneMappingMethod: None (no tone mapping)</description></item>
    /// <item><description>DesiredIntensityTarget: 0 (default 203 nits when tone mapping enabled)</description></item>
    /// <item><description>PixelFormat: RGBA8 (default)</description></item>
    /// <item><description>MetadataCapture: Default (all enabled with limits)</description></item>
    /// <item><description>CmsType: Lcms2</description></item>
    /// </list>
    /// </remarks>
    public static JxlDecodeOptions Default => new()
    {
        PixelLimit = UIntPtr.Zero,
        ProgressiveMode = JxlProgressiveMode.Pass,
        ToneMappingMethod = JxlToneMappingMethod.None,
        DesiredIntensityTarget = 0,
        AdjustOrientation = true,
        RenderSpotColors = true,
        Coalescing = true,
        SkipPreview = true,
        HighPrecision = false,
        PremultiplyAlpha = false,
        DecodeExtraChannels = false,
        PixelFormat = JxlPixelFormat.Default,
        MetadataCapture = JxlMetadataCaptureOptions.Default,
        CmsType = JxlCmsType.Lcms2,
    };
}
