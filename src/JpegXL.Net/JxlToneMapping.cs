// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Tone mapping parameters for HDR content.
/// </summary>
/// <param name="IntensityTarget">Target intensity in nits.</param>
/// <param name="MinNits">Minimum nits.</param>
/// <param name="RelativeToMaxDisplay">Whether LinearBelow is relative to max display luminance.</param>
/// <param name="LinearBelow">Linear tone mapping threshold.</param>
public readonly record struct JxlToneMapping(
    float IntensityTarget,
    float MinNits,
    bool RelativeToMaxDisplay,
    float LinearBelow);
