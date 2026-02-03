// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.InteropServices;

namespace JpegXL.Net;

/// <summary>
/// Provides access to metadata boxes (EXIF, XML/XMP, JUMBF) from a decoded JPEG XL image.
/// </summary>
/// <remarks>
/// <para>This class is accessed via <see cref="JxlDecoder.Metadata"/>.</para>
/// <para><see cref="JxlDecoder.ReadInfo"/> must be called before accessing metadata.</para>
/// </remarks>
public sealed unsafe class JxlMetadata
{
    private readonly JxlDecoder _decoder;

    internal JxlMetadata(JxlDecoder decoder) => _decoder = decoder;

    // ========================================================================
    // EXIF
    // ========================================================================

    /// <summary>
    /// Gets the number of EXIF metadata boxes in the image.
    /// </summary>
    /// <remarks>
    /// <see cref="JxlDecoder.ReadInfo"/> must be called before this property is accurate.
    /// </remarks>
    public int ExifBoxCount
    {
        get
        {
            _decoder.ThrowIfDisposed();
            return (int)NativeMethods.jxl_decoder_get_exif_box_count(_decoder.Handle);
        }
    }

    /// <summary>
    /// Gets whether the image contains any EXIF metadata boxes.
    /// </summary>
    public bool HasExifBoxes => ExifBoxCount > 0;

    /// <summary>
    /// Gets EXIF data from a specific box by index.
    /// </summary>
    /// <param name="index">Zero-based box index.</param>
    /// <returns>A metadata box containing the EXIF data and compression status, or null if index is out of range or no EXIF data exists.</returns>
    /// <remarks>
    /// <para><see cref="JxlDecoder.ReadInfo"/> must be called before this method.</para>
    /// <para>Use <see cref="ExifBoxCount"/> to check the number of available boxes.</para>
    /// <para>If the box was brotli-compressed (brob box), <see cref="JxlMetadataBox.IsBrotliCompressed"/> will be true
    /// and the data must be decompressed by the caller.</para>
    /// </remarks>
    /// <exception cref="JxlException">Thrown if called before basic info is available.</exception>
    public JxlMetadataBox? GetExifBox(int index)
    {
        _decoder.ThrowIfDisposed();

        byte* dataPtr;
        UIntPtr length;
        bool isCompressed;

        var status = NativeMethods.jxl_decoder_get_exif_box_at(
            _decoder.Handle, (uint)index, &dataPtr, &length, &isCompressed);

        if (status == JxlStatus.Error || status == JxlStatus.InvalidArgument)
        {
            return null;
        }

        JxlDecoder.ThrowIfFailed(status);

        var len = (int)(nuint)length;
        var data = new byte[len];
        Marshal.Copy((IntPtr)dataPtr, data, 0, len);
        return new JxlMetadataBox(data, isCompressed);
    }

    // ========================================================================
    // XML/XMP
    // ========================================================================

    /// <summary>
    /// Gets the number of XML/XMP metadata boxes in the image.
    /// </summary>
    /// <remarks>
    /// <see cref="JxlDecoder.ReadInfo"/> must be called before this property is accurate.
    /// </remarks>
    public int XmlBoxCount
    {
        get
        {
            _decoder.ThrowIfDisposed();
            return (int)NativeMethods.jxl_decoder_get_xml_box_count(_decoder.Handle);
        }
    }

    /// <summary>
    /// Gets whether the image contains any XML/XMP metadata boxes.
    /// </summary>
    public bool HasXmlBoxes => XmlBoxCount > 0;

    /// <summary>
    /// Gets XML/XMP data from a specific box by index.
    /// </summary>
    /// <param name="index">Zero-based box index.</param>
    /// <returns>A metadata box containing the XML data and compression status, or null if index is out of range or no XML data exists.</returns>
    /// <remarks>
    /// <para><see cref="JxlDecoder.ReadInfo"/> must be called before this method.</para>
    /// <para>Use <see cref="XmlBoxCount"/> to check the number of available boxes.</para>
    /// <para>If the box was brotli-compressed (brob box), <see cref="JxlMetadataBox.IsBrotliCompressed"/> will be true
    /// and the data must be decompressed by the caller.</para>
    /// </remarks>
    /// <exception cref="JxlException">Thrown if called before basic info is available.</exception>
    public JxlMetadataBox? GetXmlBox(int index)
    {
        _decoder.ThrowIfDisposed();

        byte* dataPtr;
        UIntPtr length;
        bool isCompressed;

        var status = NativeMethods.jxl_decoder_get_xml_box_at(
            _decoder.Handle, (uint)index, &dataPtr, &length, &isCompressed);

        if (status == JxlStatus.Error || status == JxlStatus.InvalidArgument)
        {
            return null;
        }

        JxlDecoder.ThrowIfFailed(status);

        var len = (int)(nuint)length;
        var data = new byte[len];
        Marshal.Copy((IntPtr)dataPtr, data, 0, len);
        return new JxlMetadataBox(data, isCompressed);
    }

    // ========================================================================
    // JUMBF
    // ========================================================================

    /// <summary>
    /// Gets the number of JUMBF metadata boxes in the image.
    /// </summary>
    /// <remarks>
    /// <see cref="JxlDecoder.ReadInfo"/> must be called before this property is accurate.
    /// </remarks>
    public int JumbfBoxCount
    {
        get
        {
            _decoder.ThrowIfDisposed();
            return (int)NativeMethods.jxl_decoder_get_jumbf_box_count(_decoder.Handle);
        }
    }

    /// <summary>
    /// Gets whether the image contains any JUMBF metadata boxes.
    /// </summary>
    public bool HasJumbfBoxes => JumbfBoxCount > 0;

    /// <summary>
    /// Gets JUMBF data from a specific box by index.
    /// </summary>
    /// <param name="index">Zero-based box index.</param>
    /// <returns>A metadata box containing the JUMBF data and compression status, or null if index is out of range or no JUMBF data exists.</returns>
    /// <remarks>
    /// <para><see cref="JxlDecoder.ReadInfo"/> must be called before this method.</para>
    /// <para>Use <see cref="JumbfBoxCount"/> to check the number of available boxes.</para>
    /// <para>If the box was brotli-compressed (brob box), <see cref="JxlMetadataBox.IsBrotliCompressed"/> will be true
    /// and the data must be decompressed by the caller.</para>
    /// </remarks>
    /// <exception cref="JxlException">Thrown if called before basic info is available.</exception>
    public JxlMetadataBox? GetJumbfBox(int index)
    {
        _decoder.ThrowIfDisposed();

        byte* dataPtr;
        UIntPtr length;
        bool isCompressed;

        var status = NativeMethods.jxl_decoder_get_jumbf_box_at(
            _decoder.Handle, (uint)index, &dataPtr, &length, &isCompressed);

        if (status == JxlStatus.Error || status == JxlStatus.InvalidArgument)
        {
            return null;
        }

        JxlDecoder.ThrowIfFailed(status);

        var len = (int)(nuint)length;
        var data = new byte[len];
        Marshal.Copy((IntPtr)dataPtr, data, 0, len);
        return new JxlMetadataBox(data, isCompressed);
    }
}
