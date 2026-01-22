// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Extension properties for JxlBasicInfo.
/// </summary>
public partial struct JxlBasicInfo
{
    /// <summary>
    /// Gets whether the image has an alpha channel.
    /// </summary>
    public bool HasAlpha => NumExtraChannels > 0;

    /// <summary>
    /// Gets whether the image is animated.
    /// </summary>
    public bool IsAnimated => HaveAnimation;
}
