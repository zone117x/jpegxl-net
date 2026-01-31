// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Animation parameters for a JPEG XL image.
/// </summary>
/// <param name="TpsNumerator">Ticks per second numerator.</param>
/// <param name="TpsDenominator">Ticks per second denominator.</param>
/// <param name="NumLoops">Number of loops (0 = infinite).</param>
public readonly record struct JxlAnimation(uint TpsNumerator, uint TpsDenominator, uint NumLoops);
