// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.InteropServices;

namespace JpegXL.Net.Native;

/// <summary>
/// P/Invoke declarations for the jxlrs native library.
/// </summary>
internal static class NativeMethods
{
    private const string LibraryName = "jxlrs";

    // ========================================================================
    // Library Info
    // ========================================================================

    /// <summary>
    /// Returns the library version as a packed integer.
    /// Format: (major &lt;&lt; 24) | (minor &lt;&lt; 16) | (patch &lt;&lt; 8)
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint GetVersion();

    // ========================================================================
    // Error Handling
    // ========================================================================

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_get_last_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr GetLastError(IntPtr buffer, UIntPtr bufferSize);

    /// <summary>
    /// Clears the last error message.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_clear_last_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ClearLastError();

    // ========================================================================
    // Signature Check
    // ========================================================================

    /// <summary>
    /// Checks if data appears to be a JPEG XL file.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_signature_check", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsSignature SignatureCheck(IntPtr data, UIntPtr size);

    /// <summary>
    /// Checks if data appears to be a JPEG XL file.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_signature_check", CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe JxlrsSignature SignatureCheck(byte* data, UIntPtr size);

    // ========================================================================
    // Decoder Lifecycle
    // ========================================================================

    /// <summary>
    /// Creates a new decoder instance.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr DecoderCreate();

    /// <summary>
    /// Destroys a decoder instance and frees its resources.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DecoderDestroy(IntPtr decoder);

    /// <summary>
    /// Resets the decoder to its initial state.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_reset", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderReset(IntPtr decoder);

    // ========================================================================
    // Input
    // ========================================================================

    /// <summary>
    /// Sets the input data for the decoder (one-shot decoding).
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_set_input", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderSetInput(IntPtr decoder, IntPtr data, UIntPtr size);

    /// <summary>
    /// Sets the input data for the decoder (one-shot decoding).
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_set_input", CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe JxlrsStatus DecoderSetInput(IntPtr decoder, byte* data, UIntPtr size);

    // ========================================================================
    // Configuration
    // ========================================================================

    /// <summary>
    /// Sets the desired output pixel format.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_set_pixel_format", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderSetPixelFormat(IntPtr decoder, ref JxlrsPixelFormat format);

    // ========================================================================
    // Decoding - Basic Info
    // ========================================================================

    /// <summary>
    /// Decodes the image header and retrieves basic info.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_read_info", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderReadInfo(IntPtr decoder, out JxlrsBasicInfo info);

    /// <summary>
    /// Gets the number of extra channels.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_get_extra_channel_count", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint DecoderGetExtraChannelCount(IntPtr decoder);

    /// <summary>
    /// Gets info about an extra channel.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_get_extra_channel_info", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderGetExtraChannelInfo(
        IntPtr decoder,
        uint index,
        out JxlrsExtraChannelInfo info);

    // ========================================================================
    // Decoding - Pixels
    // ========================================================================

    /// <summary>
    /// Calculates the required buffer size for decoded pixels.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_get_buffer_size", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr DecoderGetBufferSize(IntPtr decoder);

    /// <summary>
    /// Decodes pixels into the provided buffer.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_get_pixels", CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlrsStatus DecoderGetPixels(IntPtr decoder, IntPtr buffer, UIntPtr bufferSize);

    /// <summary>
    /// Decodes pixels into the provided buffer.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "jxlrs_decoder_get_pixels", CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe JxlrsStatus DecoderGetPixels(IntPtr decoder, byte* buffer, UIntPtr bufferSize);
}
