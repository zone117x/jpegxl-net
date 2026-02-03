// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Extension methods for <see cref="JxlMetadataCaptureOptions"/>.
/// </summary>
public partial struct JxlMetadataCaptureOptions
{
    /// <summary>
    /// Default size limit for EXIF boxes (1 MB).
    /// </summary>
    public const ulong DefaultExifSizeLimit = 1024 * 1024;

    /// <summary>
    /// Default size limit for XML/XMP boxes (1 MB).
    /// </summary>
    public const ulong DefaultXmlSizeLimit = 1024 * 1024;

    /// <summary>
    /// Default size limit for JUMBF boxes (16 MB).
    /// </summary>
    public const ulong DefaultJumbfSizeLimit = 16 * 1024 * 1024;

    /// <summary>
    /// Gets default options with all metadata capture enabled and default size limits.
    /// </summary>
    /// <remarks>
    /// Default values:
    /// <list type="bullet">
    /// <item><description>CaptureExif: true</description></item>
    /// <item><description>CaptureXml: true</description></item>
    /// <item><description>CaptureJumbf: true</description></item>
    /// <item><description>ExifSizeLimit: 1 MB</description></item>
    /// <item><description>XmlSizeLimit: 1 MB</description></item>
    /// <item><description>JumbfSizeLimit: 16 MB</description></item>
    /// </list>
    /// </remarks>
    public static JxlMetadataCaptureOptions Default => new()
    {
        CaptureExif = true,
        CaptureXml = true,
        CaptureJumbf = true,
        ExifSizeLimit = DefaultExifSizeLimit,
        XmlSizeLimit = DefaultXmlSizeLimit,
        JumbfSizeLimit = DefaultJumbfSizeLimit,
    };

    /// <summary>
    /// Gets options with all metadata capture disabled.
    /// </summary>
    /// <remarks>
    /// Use this when you don't need metadata and want to minimize memory usage.
    /// </remarks>
    public static JxlMetadataCaptureOptions NoCapture => new()
    {
        CaptureExif = false,
        CaptureXml = false,
        CaptureJumbf = false,
        ExifSizeLimit = 0,
        XmlSizeLimit = 0,
        JumbfSizeLimit = 0,
    };

    /// <summary>
    /// Gets options with only EXIF capture enabled.
    /// </summary>
    public static JxlMetadataCaptureOptions ExifOnly => new()
    {
        CaptureExif = true,
        CaptureXml = false,
        CaptureJumbf = false,
        ExifSizeLimit = DefaultExifSizeLimit,
        XmlSizeLimit = 0,
        JumbfSizeLimit = 0,
    };

    /// <summary>
    /// Gets options with only XML/XMP capture enabled.
    /// </summary>
    public static JxlMetadataCaptureOptions XmlOnly => new()
    {
        CaptureExif = false,
        CaptureXml = true,
        CaptureJumbf = false,
        ExifSizeLimit = 0,
        XmlSizeLimit = DefaultXmlSizeLimit,
        JumbfSizeLimit = 0,
    };
}
