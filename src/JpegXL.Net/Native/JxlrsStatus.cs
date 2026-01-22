// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net.Native;

/// <summary>
/// Status codes returned by decoder functions.
/// </summary>
public enum JxlrsStatus
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// An error occurred. Call <c>jxlrs_get_last_error</c> for details.
    /// </summary>
    Error = 1,

    /// <summary>
    /// The decoder needs more input data.
    /// </summary>
    NeedMoreInput = 2,

    /// <summary>
    /// Invalid argument passed to function.
    /// </summary>
    InvalidArgument = 3,

    /// <summary>
    /// Buffer too small for output.
    /// </summary>
    BufferTooSmall = 4,

    /// <summary>
    /// Decoder is in an invalid state for this operation.
    /// </summary>
    InvalidState = 5,
}
