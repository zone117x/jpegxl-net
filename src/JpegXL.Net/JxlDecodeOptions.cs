// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Options for decoding JPEG XL images.
/// </summary>
public sealed class JxlDecodeOptions
{
    /// <summary>
    /// Gets or sets whether to adjust image orientation based on EXIF data.
    /// Default: true
    /// </summary>
    public bool AdjustOrientation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to render spot colors.
    /// Default: true
    /// </summary>
    public bool RenderSpotColors { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to coalesce animation frames.
    /// Default: true
    /// </summary>
    public bool Coalescing { get; set; } = true;

    /// <summary>
    /// Gets or sets the desired intensity target for HDR content (in nits).
    /// Set to null to use the image's native intensity target.
    /// Default: null
    /// </summary>
    public float? DesiredIntensityTarget { get; set; }

    /// <summary>
    /// Gets or sets whether to skip the preview image.
    /// Default: true
    /// </summary>
    public bool SkipPreview { get; set; } = true;

    /// <summary>
    /// Gets or sets the progressive decoding mode.
    /// Default: Pass
    /// </summary>
    public JxlProgressiveMode ProgressiveMode { get; set; } = JxlProgressiveMode.Pass;

    /// <summary>
    /// Gets or sets whether to enable output rendering.
    /// Default: true
    /// </summary>
    public bool EnableOutput { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of pixels to decode.
    /// Set to null for no limit.
    /// Default: null
    /// </summary>
    public ulong? PixelLimit { get; set; }

    /// <summary>
    /// Gets or sets whether to use high precision mode for decoding.
    /// When false (default), uses lower precision settings that match libjxl's default.
    /// When true, uses higher precision at the cost of performance.
    /// </summary>
    public bool HighPrecision { get; set; }

    /// <summary>
    /// Gets or sets whether to premultiply alpha in the output.
    /// When false (default), outputs straight (non-premultiplied) alpha.
    /// When true, multiplies RGB by alpha before writing to output buffer.
    /// This is useful for UI frameworks that expect premultiplied alpha.
    /// </summary>
    public bool PremultiplyAlpha { get; set; }

    /// <summary>
    /// Gets a default options instance.
    /// </summary>
    public static JxlDecodeOptions Default => new();
}
