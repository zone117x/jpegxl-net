namespace JpegXL.Net;

/// <summary>
/// ICC profile class/device type.
/// </summary>
public enum IccProfileClass
{
    /// <summary>Unknown or unrecognized profile class.</summary>
    Unknown = 0,
    /// <summary>Input device profile (scanner, camera).</summary>
    Input,
    /// <summary>Display device profile (monitor).</summary>
    Display,
    /// <summary>Output device profile (printer).</summary>
    Output,
    /// <summary>Device link profile.</summary>
    DeviceLink,
    /// <summary>Color space conversion profile.</summary>
    ColorSpace,
    /// <summary>Abstract profile.</summary>
    Abstract,
    /// <summary>Named color profile.</summary>
    NamedColor
}

/// <summary>
/// ICC profile data color space type.
/// </summary>
public enum IccColorSpaceType
{
    /// <summary>Unknown or unrecognized color space.</summary>
    Unknown = 0,
    /// <summary>RGB color space.</summary>
    Rgb,
    /// <summary>Grayscale color space.</summary>
    Gray,
    /// <summary>CMYK color space.</summary>
    Cmyk,
    /// <summary>XYZ color space.</summary>
    Xyz,
    /// <summary>Lab color space.</summary>
    Lab
}

/// <summary>
/// ICC rendering intent.
/// </summary>
public enum IccRenderingIntent
{
    /// <summary>Perceptual rendering intent.</summary>
    Perceptual = 0,
    /// <summary>Media-relative colorimetric rendering intent.</summary>
    RelativeColorimetric = 1,
    /// <summary>Saturation rendering intent.</summary>
    Saturation = 2,
    /// <summary>ICC-absolute colorimetric rendering intent.</summary>
    AbsoluteColorimetric = 3
}

/// <summary>
/// Parsed ICC profile header information (first 128 bytes).
/// </summary>
public readonly struct IccHeaderInfo
{
    /// <summary>
    /// Profile class/device type (display, input, output, etc.).
    /// </summary>
    public IccProfileClass ProfileClass { get; }

    /// <summary>
    /// Data color space (RGB, Gray, CMYK, etc.).
    /// </summary>
    public IccColorSpaceType ColorSpace { get; }

    /// <summary>
    /// ICC profile version (e.g., 4.3.0.0).
    /// </summary>
    public Version IccVersion { get; }

    /// <summary>
    /// Rendering intent stored in the profile.
    /// </summary>
    public IccRenderingIntent RenderingIntent { get; }

    /// <summary>
    /// Profile size in bytes.
    /// </summary>
    public uint ProfileSize { get; }

    internal IccHeaderInfo(
        IccProfileClass profileClass,
        IccColorSpaceType colorSpace,
        Version iccVersion,
        IccRenderingIntent renderingIntent,
        uint profileSize)
    {
        ProfileClass = profileClass;
        ColorSpace = colorSpace;
        IccVersion = iccVersion;
        RenderingIntent = renderingIntent;
        ProfileSize = profileSize;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"ICC {IccVersion.Major}.{IccVersion.Minor} {ProfileClass} {ColorSpace}";
    }
}
