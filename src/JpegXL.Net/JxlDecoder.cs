// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.InteropServices;

namespace JpegXL.Net;

/// <summary>
/// JPEG XL decoder for decoding JXL images.
/// </summary>
/// <remarks>
/// This class wraps the native jxl_ffi decoder and provides a managed interface
/// for decoding JPEG XL images. It implements <see cref="IDisposable"/> and
/// should be disposed when no longer needed.
/// </remarks>
public sealed unsafe class JxlDecoder : IDisposable
{
    private NativeDecoderHandle* _handle;
    private bool _disposed;
    private JxlBasicInfo? _basicInfo;

    /// <summary>
    /// Creates a new JPEG XL decoder with default options.
    /// </summary>
    /// <exception cref="JxlException">Thrown if decoder creation fails.</exception>
    public JxlDecoder() : this(null)
    {
    }

    /// <summary>
    /// Creates a new JPEG XL decoder with the specified options.
    /// </summary>
    /// <param name="options">The decode options, or null for defaults.</param>
    /// <exception cref="JxlException">Thrown if decoder creation fails.</exception>
    public JxlDecoder(JxlDecodeOptions? options)
    {
        var opts = options ?? JxlDecodeOptions.Default;
        _handle = NativeMethods.jxl_decoder_create_with_options(&opts);

        if (_handle == null)
        {
            throw new JxlException(JxlStatus.Error, "Failed to create decoder");
        }
    }

    /// <summary>
    /// Gets the basic image information after decoding the header.
    /// </summary>
    /// <remarks>
    /// This property is only valid after calling <see cref="SetInput"/> and <see cref="ReadInfo"/>.
    /// </remarks>
    public JxlBasicInfo? BasicInfo => _basicInfo;

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
    public bool HasAlpha => (_basicInfo?.NumExtraChannels ?? 0) > 0;

    /// <summary>
    /// Gets whether the image is animated.
    /// </summary>
    public bool IsAnimated => _basicInfo?.HaveAnimation ?? false;

    /// <summary>
    /// Sets the input data for decoding.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if setting input fails.</exception>
    public void SetInput(byte[] data)
    {
#if NETSTANDARD2_0
        if (data == null) throw new ArgumentNullException(nameof(data));
#else
        ArgumentNullException.ThrowIfNull(data);
#endif
        SetInput(data.AsSpan());
    }

    /// <summary>
    /// Sets the input data for decoding.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <exception cref="JxlException">Thrown if setting input fails.</exception>
    public void SetInput(ReadOnlySpan<byte> data)
    {
        Reset();
        AppendInput(data);
    }

    /// <summary>
    /// Sets the desired output pixel format.
    /// </summary>
    /// <param name="format">The pixel format to use for decoded output.</param>
    /// <exception cref="JxlException">Thrown if setting pixel format fails.</exception>
    public void SetPixelFormat(JxlPixelFormat format)
    {
        ThrowIfDisposed();
        var status = NativeMethods.jxl_decoder_set_pixel_format(_handle, &format);
        ThrowIfFailed(status);
    }

    /// <summary>
    /// Reads the image header and basic info.
    /// </summary>
    /// <returns>The basic image information.</returns>
    /// <exception cref="JxlException">Thrown if reading header fails.</exception>
    public JxlBasicInfo ReadInfo()
    {
        ThrowIfDisposed();

        // If we already have cached info, return it
        if (_basicInfo.HasValue)
        {
            return _basicInfo.Value;
        }

        // Use streaming API to read info
        var evt = Process();
        while (evt == JxlDecoderEvent.NeedMoreInput)
        {
            throw new JxlException(JxlStatus.NeedMoreInput, "Incomplete header data - use SetInput with complete data or use streaming API");
        }

        if (evt != JxlDecoderEvent.HaveBasicInfo)
        {
            throw new JxlException(JxlStatus.Error, $"Unexpected decoder event: {evt}");
        }

        return GetBasicInfo();
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
        return (int)(uint)NativeMethods.jxl_decoder_get_buffer_size(_handle);
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
#if NETSTANDARD2_0
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
#else
        ArgumentNullException.ThrowIfNull(buffer);
#endif
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

        // Ensure we have basic info
        if (!_basicInfo.HasValue)
        {
            ReadInfo();
        }

        // Process until we get NeedOutputBuffer
        var evt = Process();
        while (evt == JxlDecoderEvent.NeedMoreInput)
        {
            throw new JxlException(JxlStatus.NeedMoreInput, "Incomplete data - use SetInput with complete data or use streaming API");
        }

        // Skip frame header event if present
        if (evt == JxlDecoderEvent.HaveFrameHeader)
        {
            evt = Process();
        }

        if (evt != JxlDecoderEvent.NeedOutputBuffer)
        {
            throw new JxlException(JxlStatus.Error, $"Unexpected decoder event: {evt}");
        }

        // Decode pixels
        evt = ReadPixels(buffer);
        if (evt == JxlDecoderEvent.NeedMoreInput)
        {
            throw new JxlException(JxlStatus.NeedMoreInput, "Incomplete pixel data");
        }
        if (evt != JxlDecoderEvent.FrameComplete)
        {
            throw new JxlException(JxlStatus.Error, $"Unexpected decoder event after ReadPixels: {evt}");
        }
    }

    /// <summary>
    /// Gets the number of extra channels in the image.
    /// </summary>
    /// <returns>The number of extra channels.</returns>
    public int GetExtraChannelCount()
    {
        ThrowIfDisposed();
        return (int)NativeMethods.jxl_decoder_get_extra_channel_count(_handle);
    }

    /// <summary>
    /// Gets information about an extra channel.
    /// </summary>
    /// <param name="index">The index of the extra channel.</param>
    /// <returns>Information about the extra channel.</returns>
    /// <exception cref="JxlException">Thrown if the index is out of range.</exception>
    public JxlExtraChannelInfo GetExtraChannelInfo(int index)
    {
        ThrowIfDisposed();

        JxlExtraChannelInfo info;
        var status = NativeMethods.jxl_decoder_get_extra_channel_info(_handle, (uint)index, &info);
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
        var status = NativeMethods.jxl_decoder_reset(_handle);
        ThrowIfFailed(status);
        _basicInfo = null;
    }

    // ========================================================================
    // Streaming API
    // ========================================================================

    /// <summary>
    /// Appends input data for incremental decoding.
    /// </summary>
    /// <param name="data">Additional JXL-encoded data to append.</param>
    /// <remarks>
    /// Does not reset the decoder state. Use this for streaming scenarios where
    /// data arrives incrementally.
    /// </remarks>
    /// <exception cref="JxlException">Thrown if appending input fails.</exception>
    public void AppendInput(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        fixed (byte* ptr = data)
        {
            var status = NativeMethods.jxl_decoder_append_input(_handle, ptr, (UIntPtr)data.Length);
            ThrowIfFailed(status);
        }
    }

    /// <summary>
    /// Appends input data for incremental decoding.
    /// </summary>
    /// <param name="data">Additional JXL-encoded data to append.</param>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if appending input fails.</exception>
    public void AppendInput(byte[] data)
    {
#if NETSTANDARD2_0
        if (data == null) throw new ArgumentNullException(nameof(data));
#else
        ArgumentNullException.ThrowIfNull(data);
#endif
        AppendInput(data.AsSpan());
    }

    /// <summary>
    /// Processes the current input data and returns the next decoder event.
    /// </summary>
    /// <returns>The event indicating what action should be taken next.</returns>
    /// <remarks>
    /// <para>
    /// This is the main entry point for streaming decoding. Call this method
    /// repeatedly to drive the decode process. Based on the returned event:
    /// </para>
    /// <list type="bullet">
    /// <item><term><see cref="JxlDecoderEvent.NeedMoreInput"/></term>
    /// <description>Call <see cref="AppendInput"/> with more data.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.HaveBasicInfo"/></term>
    /// <description>Call <see cref="GetBasicInfo"/> to retrieve image metadata.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.HaveFrameHeader"/></term>
    /// <description>Frame header is available for processing.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.NeedOutputBuffer"/></term>
    /// <description>Call <see cref="ReadPixels"/> to decode pixels.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.FrameComplete"/></term>
    /// <description>A frame has been fully decoded.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.Complete"/></term>
    /// <description>All frames have been decoded.</description></item>
    /// <item><term><see cref="JxlDecoderEvent.Error"/></term>
    /// <description>An error occurred. Check the exception for details.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="JxlException">Thrown if an error occurs during processing.</exception>
    public JxlDecoderEvent Process()
    {
        ThrowIfDisposed();
        var evt = NativeMethods.jxl_decoder_process(_handle);
        if (evt == JxlDecoderEvent.Error)
        {
            var message = GetLastError();
            throw new JxlException(JxlStatus.Error, message);
        }
        return (JxlDecoderEvent)evt;
    }

    /// <summary>
    /// Gets the basic image information after <see cref="Process"/> returns 
    /// <see cref="JxlDecoderEvent.HaveBasicInfo"/>.
    /// </summary>
    /// <returns>The basic image information.</returns>
    /// <exception cref="JxlException">Thrown if info is not yet available.</exception>
    public JxlBasicInfo GetBasicInfo()
    {
        ThrowIfDisposed();

        JxlBasicInfo info;
        var status = NativeMethods.jxl_decoder_get_basic_info(_handle, &info);
        ThrowIfFailed(status);

        _basicInfo = info;
        return info;
    }

    /// <summary>
    /// Gets the current frame header after <see cref="Process"/> returns
    /// <see cref="JxlDecoderEvent.HaveFrameHeader"/>.
    /// </summary>
    /// <returns>The frame header information.</returns>
    /// <exception cref="JxlException">Thrown if frame header is not yet available.</exception>
    public JxlFrameHeader GetFrameHeader()
    {
        ThrowIfDisposed();

        JxlFrameHeader header;
        var status = NativeMethods.jxl_decoder_get_frame_header(_handle, &header);
        ThrowIfFailed(status);

        return header;
    }

    /// <summary>
    /// Gets the current frame's name after <see cref="Process"/> returns
    /// <see cref="JxlDecoderEvent.HaveFrameHeader"/>.
    /// </summary>
    /// <returns>The frame name, or an empty string if the frame has no name.</returns>
    /// <remarks>
    /// Most frames do not have names. Check <see cref="JxlFrameHeader.NameLength"/>
    /// to determine if a name exists before calling this method.
    /// </remarks>
    public string GetFrameName()
    {
        ThrowIfDisposed();

        // First call with null buffer to get required size
        uint length = NativeMethods.jxl_decoder_get_frame_name(_handle, null, 0);

        if (length == 0)
            return string.Empty;

        // Allocate buffer and get the name
        var buffer = new byte[length];
        fixed (byte* ptr = buffer)
        {
            uint written = NativeMethods.jxl_decoder_get_frame_name(_handle, ptr, length);
            if (written == 0)
                return string.Empty;

#if NETSTANDARD2_0
            return System.Text.Encoding.UTF8.GetString(buffer, 0, (int)written);
#else
            return System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, (int)written));
#endif
        }
    }

    /// <summary>
    /// Decodes pixels into the provided buffer during streaming decode.
    /// </summary>
    /// <param name="buffer">The buffer to write decoded pixels to.</param>
    /// <returns>The event indicating what happened during pixel decoding.</returns>
    /// <remarks>
    /// Call this method after <see cref="Process"/> returns <see cref="JxlDecoderEvent.NeedOutputBuffer"/>.
    /// The returned event indicates whether more data is needed or if the frame is complete.
    /// </remarks>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public JxlDecoderEvent ReadPixels(Span<byte> buffer)
    {
        ThrowIfDisposed();

        fixed (byte* ptr = buffer)
        {
            var evt = NativeMethods.jxl_decoder_read_pixels(_handle, ptr, (UIntPtr)buffer.Length);
            if (evt == JxlDecoderEvent.Error)
            {
                var message = GetLastError();
                throw new JxlException(JxlStatus.Error, message);
            }
            return (JxlDecoderEvent)evt;
        }
    }

    /// <summary>
    /// Gets the required buffer size for a specific extra channel.
    /// </summary>
    /// <param name="index">The extra channel index (0-based).</param>
    /// <returns>The required buffer size in bytes, or 0 if invalid.</returns>
    public nuint GetExtraChannelBufferSize(uint index)
    {
        ThrowIfDisposed();
        return NativeMethods.jxl_decoder_get_extra_channel_buffer_size(_handle, index);
    }

    /// <summary>
    /// Decodes pixels with extra channels into separate buffers.
    /// </summary>
    /// <param name="colorBuffer">Buffer for color data (RGB/RGBA/etc.).</param>
    /// <param name="extraBuffers">Array of buffers for extra channels.</param>
    /// <returns>The event indicating what happened during pixel decoding.</returns>
    /// <remarks>
    /// <para>
    /// Set <see cref="JxlDecodeOptions.DecodeExtraChannels"/> to true when creating the decoder
    /// to enable extra channel decoding.
    /// </para>
    /// <para>
    /// Extra channels are decoded in order. Pass null for a buffer to skip that channel.
    /// </para>
    /// </remarks>
    public JxlDecoderEvent ReadPixelsWithExtraChannels(Span<byte> colorBuffer, Span<byte[]?> extraBuffers)
    {
        ThrowIfDisposed();

        var numExtra = extraBuffers.Length;
        var extraPtrs = stackalloc byte*[numExtra];
        var extraSizes = stackalloc nuint[numExtra];
        
        // Pin all the extra buffers and set up pointers
        var handles = new GCHandle[numExtra];
        try
        {
            for (int i = 0; i < numExtra; i++)
            {
                var buffer = extraBuffers[i];
                if (buffer != null)
                {
                    handles[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    extraPtrs[i] = (byte*)handles[i].AddrOfPinnedObject();
                    extraSizes[i] = (nuint)buffer.Length;
                }
                else
                {
                    extraPtrs[i] = null;
                    extraSizes[i] = 0;
                }
            }

            fixed (byte* colorPtr = colorBuffer)
            {
                var evt = NativeMethods.jxl_decoder_read_pixels_with_extra_channels(
                    _handle,
                    colorPtr,
                    (UIntPtr)colorBuffer.Length,
                    extraPtrs,
                    extraSizes,
                    (UIntPtr)numExtra);
                
                if (evt == JxlDecoderEvent.Error)
                {
                    var message = GetLastError();
                    throw new JxlException(JxlStatus.Error, message);
                }
                return (JxlDecoderEvent)evt;
            }
        }
        finally
        {
            // Free all pinned handles
            for (int i = 0; i < numExtra; i++)
            {
                if (handles[i].IsAllocated)
                {
                    handles[i].Free();
                }
            }
        }
    }

    /// <summary>
    /// Gets whether there are more frames to decode in an animated image.
    /// </summary>
    /// <returns>True if more frames are available, false otherwise.</returns>
    public bool HasMoreFrames()
    {
        ThrowIfDisposed();
        return NativeMethods.jxl_decoder_has_more_frames(_handle);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the decoder.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != null)
            {
                NativeMethods.jxl_decoder_destroy(_handle);
                _handle = null;
            }
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(nameof(JxlDecoder));
#endif
    }

    private static void ThrowIfFailed(JxlStatus status)
    {
        if (status != JxlStatus.Success)
        {
            var message = GetLastError();
            throw new JxlException((JxlStatus)status, message);
        }
    }

    private static string? GetLastError()
    {
        // Get required length
        var length = (int)(uint)NativeMethods.jxl_get_last_error(null, UIntPtr.Zero);
        if (length == 0)
        {
            return null;
        }

        // Allocate buffer and get message
        var buffer = stackalloc byte[length + 1];
        NativeMethods.jxl_get_last_error(buffer, (UIntPtr)(length + 1));
        
#if NETSTANDARD2_0
        // PtrToStringUTF8 not available in netstandard2.0, use ANSI as fallback
        return Marshal.PtrToStringAnsi((IntPtr)buffer);
#else
        return Marshal.PtrToStringUTF8((IntPtr)buffer);
#endif
    }
}
