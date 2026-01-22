// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.InteropServices;
using System.Text;
using JpegXL.Net.Native;

namespace JpegXL.Net;

/// <summary>
/// Exception thrown when a JPEG XL decoding operation fails.
/// </summary>
public class JxlException : Exception
{
    /// <summary>
    /// The status code from the native library.
    /// </summary>
    public JxlrsStatus Status { get; }

    /// <summary>
    /// Creates a new JxlException.
    /// </summary>
    public JxlException(JxlrsStatus status, string? message = null)
        : base(message ?? GetDefaultMessage(status))
    {
        Status = status;
    }

    /// <summary>
    /// Creates a new JxlException with an inner exception.
    /// </summary>
    public JxlException(JxlrsStatus status, string message, Exception innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    private static string GetDefaultMessage(JxlrsStatus status) => status switch
    {
        JxlrsStatus.Success => "Operation completed successfully",
        JxlrsStatus.Error => "An error occurred",
        JxlrsStatus.NeedMoreInput => "More input data is needed",
        JxlrsStatus.InvalidArgument => "Invalid argument",
        JxlrsStatus.BufferTooSmall => "Buffer too small",
        JxlrsStatus.InvalidState => "Invalid decoder state",
        _ => $"Unknown error (status {(int)status})"
    };
}
