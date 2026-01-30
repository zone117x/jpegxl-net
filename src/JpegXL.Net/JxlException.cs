// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;

namespace JpegXL.Net;

/// <summary>
/// Exception thrown when a JPEG XL decoding operation fails.
/// </summary>
public class JxlException : Exception
{
    /// <summary>
    /// The status code from the native library.
    /// </summary>
    public JxlStatus Status { get; }

    /// <summary>
    /// Creates a new JxlException.
    /// </summary>
    public JxlException(JxlStatus status, string? message = null)
        : base(message ?? GetDefaultMessage(status))
    {
        Status = status;
    }

    /// <summary>
    /// Creates a new JxlException with an inner exception.
    /// </summary>
    public JxlException(JxlStatus status, string message, Exception innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    private static string GetDefaultMessage(JxlStatus status) => status switch
    {
        JxlStatus.Success => "Operation completed successfully",
        JxlStatus.Error => "An error occurred",
        JxlStatus.NeedMoreInput => "More input data is needed",
        JxlStatus.InvalidArgument => "Invalid argument",
        JxlStatus.BufferTooSmall => "Buffer too small",
        JxlStatus.InvalidState => "Invalid decoder state",
        _ => $"Unknown error (status {(int)status})"
    };
}
