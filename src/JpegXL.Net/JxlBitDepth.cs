// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Represents the bit depth of a JPEG XL image.
/// </summary>
public abstract record JxlBitDepth
{
    // Private constructor prevents external inheritance
    private JxlBitDepth() { }

    /// <summary>
    /// Number of bits per sample.
    /// </summary>
    public uint BitsPerSample => this switch
    {
        Int i => i.Bits,
        Float f => f.Bits,
        _ => 0
    };

    /// <summary>
    /// Integer bit depth.
    /// </summary>
    /// <param name="Bits">Number of bits per sample.</param>
    public record Int(uint Bits) : JxlBitDepth;

    /// <summary>
    /// Floating-point bit depth.
    /// </summary>
    /// <param name="Bits">Number of bits per sample.</param>
    /// <param name="ExponentBitsPerSample">Number of exponent bits per sample.</param>
    public record Float(uint Bits, uint ExponentBitsPerSample) : JxlBitDepth;
}
