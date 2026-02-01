// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System.Collections.Generic;
using System.Linq;

namespace JpegXL.Net;

/// <summary>
/// Basic information about a JPEG XL image.
/// </summary>
public class JxlBasicInfo
{
    /// <summary>
    /// Image dimensions (width, height).
    /// </summary>
    public (nuint Width, nuint Height) Size { get; init; }

    /// <summary>
    /// Bit depth of the image.
    /// </summary>
    public required JxlBitDepth BitDepth { get; init; }

    /// <summary>
    /// Image orientation.
    /// </summary>
    public JxlOrientation Orientation { get; init; }

    /// <summary>
    /// Extra channels (alpha, depth, etc.).
    /// </summary>
    public required IReadOnlyList<JxlExtraChannelInfo> ExtraChannels { get; init; }

    /// <summary>
    /// Animation parameters, or null if not animated.
    /// </summary>
    public JxlAnimation? Animation { get; init; }

    /// <summary>
    /// Whether the original color profile is used.
    /// </summary>
    public bool UsesOriginalProfile { get; init; }

    /// <summary>
    /// Whether alpha is premultiplied.
    /// </summary>
    public bool AlphaPremultiplied { get; init; }

    /// <summary>
    /// Tone mapping parameters for HDR content.
    /// </summary>
    public JxlToneMapping ToneMapping { get; init; }

    /// <summary>
    /// Preview dimensions, or null if no preview.
    /// </summary>
    public (nuint Width, nuint Height)? PreviewSize { get; init; }

    /// <summary>
    /// Whether the image has an alpha channel.
    /// </summary>
    public bool HasAlpha => ExtraChannels.Any(ec => ec.ChannelType == JxlExtraChannelType.Alpha);

    /// <summary>
    /// Whether the image is animated.
    /// </summary>
    public bool IsAnimated => Animation != null;

    /// <summary>
    /// Whether the image is HDR based on its intensity target.
    /// Note: Floating-point bit depth alone does not indicate HDR - it's about precision, not dynamic range.
    /// </summary>
    public bool IsHdr => ToneMapping.IntensityTarget > 255f;
}
