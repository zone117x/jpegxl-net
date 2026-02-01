// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Extension properties for JxlBitDepth.
/// </summary>
public partial struct JxlBitDepth
{
    /// <summary>
    /// Gets whether this is an integer bit depth.
    /// </summary>
    public bool IsInteger => Type == JxlBitDepthType.Int;

    /// <summary>
    /// Gets whether this is a floating-point bit depth.
    /// </summary>
    public bool IsFloat => Type == JxlBitDepthType.Float;
}
