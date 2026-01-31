// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;

namespace JpegXL.Net;

/// <summary>
/// Represents a decoded JPEG XL image.
/// </summary>
public sealed class JxlImage : IDisposable
{
    private byte[]? _pixels;
    private bool _disposed;

    /// <summary>
    /// Gets the image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the pixel format of the decoded image.
    /// </summary>
    public JxlPixelFormat PixelFormat { get; }

    /// <summary>
    /// Gets the basic image information.
    /// </summary>
    public JxlBasicInfo BasicInfo { get; }

    /// <summary>
    /// Gets the decoded pixel data.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _pixels ?? throw new ObjectDisposedException(nameof(JxlImage));

    /// <summary>
    /// Gets the decoded pixel data as a byte array.
    /// </summary>
    public byte[] GetPixelArray() => _pixels ?? throw new ObjectDisposedException(nameof(JxlImage));

    /// <summary>
    /// Gets the number of bytes per pixel.
    /// </summary>
    public int BytesPerPixel => CalculateBytesPerPixel(PixelFormat);

    /// <summary>
    /// Gets the stride (bytes per row) of the image.
    /// </summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>
    /// Gets whether the image has an alpha channel.
    /// </summary>
    public bool HasAlpha => BasicInfo.HasAlpha;

    /// <summary>
    /// Gets whether the image is animated.
    /// </summary>
    public bool IsAnimated => BasicInfo.IsAnimated;

    private JxlImage(byte[] pixels, JxlBasicInfo info, JxlPixelFormat format)
    {
        _pixels = pixels;
        Width = (int)info.Size.Width;
        Height = (int)info.Size.Height;
        PixelFormat = format;
        BasicInfo = info;
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified byte array.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(byte[] data)
    {
        return Decode(data, JxlPixelFormat.Default);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified byte array.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="format">The desired output pixel format.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(byte[] data, JxlPixelFormat format)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        return Decode(data.AsSpan(), format);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified span.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(ReadOnlySpan<byte> data)
    {
        return Decode(data, JxlPixelFormat.Default);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified span.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="format">The desired output pixel format.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(ReadOnlySpan<byte> data, JxlPixelFormat format)
    {
        return Decode(data, format, null);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified byte array with options.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="options">The decode options.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(byte[] data, JxlDecodeOptions options)
    {
        return Decode(data, JxlPixelFormat.Default, options);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified byte array with format and options.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="format">The desired output pixel format.</param>
    /// <param name="options">The decode options.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(byte[] data, JxlPixelFormat format, JxlDecodeOptions? options)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        return Decode(data.AsSpan(), format, options);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified span with options.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="options">The decode options.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(ReadOnlySpan<byte> data, JxlDecodeOptions options)
    {
        return Decode(data, JxlPixelFormat.Default, options);
    }

    /// <summary>
    /// Decodes a JPEG XL image from the specified span with format and options.
    /// </summary>
    /// <param name="data">The JXL-encoded image data.</param>
    /// <param name="format">The desired output pixel format.</param>
    /// <param name="options">The decode options, or null for defaults.</param>
    /// <returns>A decoded image.</returns>
    /// <exception cref="JxlException">Thrown if decoding fails.</exception>
    public static JxlImage Decode(ReadOnlySpan<byte> data, JxlPixelFormat format, JxlDecodeOptions? options)
    {
        using var decoder = new JxlDecoder(options);

        decoder.SetInput(data);
        decoder.SetPixelFormat(format);

        var info = decoder.ReadInfo();
        var pixels = decoder.GetPixels();

        return new JxlImage(pixels, info, format);
    }

    /// <summary>
    /// Checks if data appears to be a JPEG XL file.
    /// </summary>
    /// <param name="data">The data to check (only first 12 bytes are needed).</param>
    /// <returns>The signature check result.</returns>
    public static unsafe JxlSignature CheckSignature(ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            return NativeMethods.jxl_signature_check(ptr, (UIntPtr)data.Length);
        }
    }

    /// <summary>
    /// Determines if the data is a valid JPEG XL file.
    /// </summary>
    /// <param name="data">The data to check.</param>
    /// <returns>True if the data is a JPEG XL codestream or container.</returns>
    public static bool IsJxl(ReadOnlySpan<byte> data)
    {
        var sig = CheckSignature(data);
        return sig == JxlSignature.Codestream || sig == JxlSignature.Container;
    }

    /// <summary>
    /// Releases the pixel buffer.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _pixels = null;
            _disposed = true;
        }
    }

    private static int CalculateBytesPerPixel(JxlPixelFormat format)
    {
        var bytesPerChannel = format.DataFormat switch
        {
            JxlDataFormat.Uint8 => 1,
            JxlDataFormat.Uint16 => 2,
            JxlDataFormat.Float16 => 2,
            JxlDataFormat.Float32 => 4,
            _ => 1
        };

        var channels = format.ColorType switch
        {
            JxlColorType.Grayscale => 1,
            JxlColorType.GrayscaleAlpha => 2,
            JxlColorType.Rgb or JxlColorType.Bgr => 3,
            JxlColorType.Rgba or JxlColorType.Bgra => 4,
            _ => 4
        };

        return bytesPerChannel * channels;
    }
}
