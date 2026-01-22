// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.InteropServices;
using System.Text;
using JpegXL.Net.Native;

namespace JpegXL.Net;

/// <summary>
/// JPEG XL decoder for decoding JXL images.
/// </summary>
/// <remarks>
/// This class wraps the native jxlrs decoder and provides a managed interface
/// for decoding JPEG XL images. It implements <see cref="IDisposable"/> and
/// should be disposed when no longer needed.
/// </remarks>
public sealed class JxlDecoder : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;
    private JxlrsBasicInfo? _basicInfo;

    /// <summary>
    /// Creates a new JPEG XL decoder.
    /// </summary>
    /// <exception cref="JxlException">Thrown if decoder creation fails.</exception>
    public JxlDecoder()
    {
        _handle = NativeMethods.DecoderCreate();
        if (_handle == IntPtr.Zero)
        {
            throw new JxlException(JxlrsStatus.Error, "Failed to create decoder");
        }
    }

    /// <summary>
    /// Gets the basic image information after decoding the header.
    /// </summary>
    /// <remarks>
    /// This property is only valid after calling <see cref="SetInput"/> and <see cref="ReadInfo"/>.
    /// </remarks>
    public JxlrsBasicInfo? BasicInfo => _basicInfo;

    /// <summary>
    /// Gets the image width in pixels.
    /// </summary>
    public int Width => (int)(_basicInfo?.Width ?? 0);

    /// <summary>
    /// Gets the image height in pixels.
    /// </summary>
    public int Height => (int)(_basicInfo?.Height ?? 0);

    /// <summary>
    /// Gets whether the image has an alpha channel.
    /// </summary>
    public bool HasAlpha => _basicInfo?.HasAlpha ?? false;

    /// <summary>
    /// Gets whether the image is animated.
    /// </summary>
    public bool IsAnimated => _basicInfo?.IsAnimated ?? false;

    /// <summary>
    /// Sets the input data for decoding.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if setting input fails.</exception>
    public void SetInput(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        SetInput(data.AsSpan());
    }

    /// <summary>
    /// Sets the input data for decoding.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <exception cref="JxlException">Thrown if setting input fails.</exception>
    public void SetInput(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        unsafe
        {
            fixed (byte* ptr = data)
            {
                var status = NativeMethods.DecoderSetInput(_handle, ptr, (UIntPtr)data.Length);
                ThrowIfFailed(status);
            }
        }

        _basicInfo = null;
    }

    /// <summary>
    /// Sets the desired output pixel format.
    /// </summary>
    /// <param name="format">The pixel format to use for decoded output.</param>
    /// <exception cref="JxlException">Thrown if setting pixel format fails.</exception>
    public void SetPixelFormat(JxlrsPixelFormat format)
    {
        ThrowIfDisposed();
        var status = NativeMethods.DecoderSetPixelFormat(_handle, ref format);
        ThrowIfFailed(status);
    }

    /// <summary>
    /// Reads the image header and basic info.
    /// </summary>
    /// <returns>The basic image information.</returns>
    /// <exception cref="JxlException">Thrown if reading header fails.</exception>
    public JxlrsBasicInfo ReadInfo()
    {
        ThrowIfDisposed();

        var status = NativeMethods.DecoderReadInfo(_handle, out var info);
        ThrowIfFailed(status);

        _basicInfo = info;
        return info;
    }

    /// <summary>
    /// Gets the required buffer size for decoded pixels.
    /// </summary>
    /// <returns>The required buffer size in bytes.</returns>
    /// <remarks>
    /// <see cref="ReadInfo"/> must be called before this method.
    /// </remarks>
    public int GetBufferSize()
    {
        ThrowIfDisposed();
        return (int)NativeMethods.DecoderGetBufferSize(_handle);
    }

    /// <summary>
    /// Decodes pixels into a new byte array.
    /// </summary>
    /// <returns>A byte array containing the decoded pixel data.</returns>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    /// <remarks>
    /// <see cref="ReadInfo"/> must be called before this method.
    /// </remarks>
    public byte[] GetPixels()
    {
        ThrowIfDisposed();

        var bufferSize = GetBufferSize();
        var buffer = new byte[bufferSize];
        GetPixels(buffer);
        return buffer;
    }

    /// <summary>
    /// Decodes pixels into the provided buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write decoded pixels to.</param>
    /// <exception cref="ArgumentNullException">Thrown if buffer is null.</exception>
    /// <exception cref="JxlException">Thrown if decoding fails or buffer is too small.</exception>
    public void GetPixels(byte[] buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        GetPixels(buffer.AsSpan());
    }

    /// <summary>
    /// Decodes pixels into the provided buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write decoded pixels to.</param>
    /// <exception cref="JxlException">Thrown if decoding fails or buffer is too small.</exception>
    public void GetPixels(Span<byte> buffer)
    {
        ThrowIfDisposed();

        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                var status = NativeMethods.DecoderGetPixels(_handle, ptr, (UIntPtr)buffer.Length);
                ThrowIfFailed(status);
            }
        }
    }

    /// <summary>
    /// Gets the number of extra channels in the image.
    /// </summary>
    /// <returns>The number of extra channels.</returns>
    public int GetExtraChannelCount()
    {
        ThrowIfDisposed();
        return (int)NativeMethods.DecoderGetExtraChannelCount(_handle);
    }

    /// <summary>
    /// Gets information about an extra channel.
    /// </summary>
    /// <param name="index">The index of the extra channel.</param>
    /// <returns>Information about the extra channel.</returns>
    /// <exception cref="JxlException">Thrown if the index is out of range.</exception>
    public JxlrsExtraChannelInfo GetExtraChannelInfo(int index)
    {
        ThrowIfDisposed();

        var status = NativeMethods.DecoderGetExtraChannelInfo(_handle, (uint)index, out var info);
        ThrowIfFailed(status);
        return info;
    }

    /// <summary>
    /// Resets the decoder to its initial state.
    /// </summary>
    /// <exception cref="JxlException">Thrown if reset fails.</exception>
    public void Reset()
    {
        ThrowIfDisposed();
        var status = NativeMethods.DecoderReset(_handle);
        ThrowIfFailed(status);
        _basicInfo = null;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the decoder.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.DecoderDestroy(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JxlDecoder));
    }

    private static void ThrowIfFailed(JxlrsStatus status)
    {
        if (status != JxlrsStatus.Success)
        {
            var message = GetLastError();
            throw new JxlException(status, message);
        }
    }

    private static string? GetLastError()
    {
        // Get required length
        var length = (int)NativeMethods.GetLastError(IntPtr.Zero, UIntPtr.Zero);
        if (length == 0)
        {
            return null;
        }

        // Allocate buffer and get message
        var buffer = Marshal.AllocHGlobal(length + 1);
        try
        {
            NativeMethods.GetLastError(buffer, (UIntPtr)(length + 1));
#if NETSTANDARD2_0
            // PtrToStringUTF8 not available in netstandard2.0, use ANSI as fallback
            return Marshal.PtrToStringAnsi(buffer);
#else
            return Marshal.PtrToStringUTF8(buffer);
#endif
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
