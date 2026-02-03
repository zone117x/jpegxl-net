using System;

namespace JpegXL.Net;

/// <summary>
/// Contains parsed EXIF metadata from an image.
/// </summary>
public sealed record ExifData
{
    // Camera info
    /// <summary>Camera manufacturer (e.g., "Canon", "Nikon").</summary>
    public string? Make { get; init; }

    /// <summary>Camera model (e.g., "Canon EOS R5").</summary>
    public string? Model { get; init; }

    /// <summary>Software used to create/edit the image.</summary>
    public string? Software { get; init; }

    // Image info
    /// <summary>Image description or title.</summary>
    public string? ImageDescription { get; init; }

    /// <summary>Copyright notice.</summary>
    public string? Copyright { get; init; }

    /// <summary>Image creator/artist.</summary>
    public string? Artist { get; init; }

    // Dates
    /// <summary>Date/time the file was last modified.</summary>
    public DateTime? DateTime { get; init; }

    /// <summary>Date/time the original image was taken.</summary>
    public DateTime? DateTimeOriginal { get; init; }

    /// <summary>Date/time the image was digitized.</summary>
    public DateTime? DateTimeDigitized { get; init; }

    // Image parameters
    /// <summary>Image orientation (rotation/flip).</summary>
    public JxlOrientation? Orientation { get; init; }

    /// <summary>Image width in pixels (from EXIF, may differ from actual).</summary>
    public uint? ImageWidth { get; init; }

    /// <summary>Image height in pixels (from EXIF, may differ from actual).</summary>
    public uint? ImageHeight { get; init; }

    // Exposure info
    /// <summary>Exposure time in seconds (e.g., 1/250).</summary>
    public ExifRational? ExposureTime { get; init; }

    /// <summary>F-number (aperture) (e.g., 2.8).</summary>
    public ExifRational? FNumber { get; init; }

    /// <summary>ISO speed rating.</summary>
    public ushort? IsoSpeedRatings { get; init; }

    /// <summary>Focal length in mm.</summary>
    public ExifRational? FocalLength { get; init; }

    /// <summary>35mm equivalent focal length.</summary>
    public ushort? FocalLengthIn35mmFilm { get; init; }

    /// <summary>Flash status and mode.</summary>
    public ushort? Flash { get; init; }

    /// <summary>Exposure program (manual, auto, etc.).</summary>
    public ushort? ExposureProgram { get; init; }

    /// <summary>Metering mode.</summary>
    public ushort? MeteringMode { get; init; }

    /// <summary>Exposure bias/compensation in EV.</summary>
    public ExifSignedRational? ExposureBias { get; init; }

    // GPS info
    /// <summary>GPS latitude (degrees, minutes, seconds).</summary>
    public ExifGpsCoordinate? GpsLatitude { get; init; }

    /// <summary>GPS longitude (degrees, minutes, seconds).</summary>
    public ExifGpsCoordinate? GpsLongitude { get; init; }

    /// <summary>GPS altitude in meters.</summary>
    public ExifRational? GpsAltitude { get; init; }

    /// <summary>GPS altitude reference (0 = above sea level, 1 = below).</summary>
    public byte? GpsAltitudeRef { get; init; }

    // Thumbnail
    /// <summary>Whether a thumbnail is embedded in the EXIF data.</summary>
    public bool HasThumbnail { get; init; }

    /// <summary>Offset of thumbnail data within the EXIF block.</summary>
    public int ThumbnailOffset { get; init; }

    /// <summary>Length of thumbnail data in bytes.</summary>
    public int ThumbnailLength { get; init; }

    // Convenience properties

    /// <summary>Whether GPS coordinates are present.</summary>
    public bool HasGps => GpsLatitude != null && GpsLongitude != null;

    /// <summary>Whether the flash fired.</summary>
    public bool? FlashFired => Flash.HasValue ? (Flash.Value & 0x01) != 0 : null;

    /// <summary>Gets the GPS latitude as decimal degrees (positive = North, negative = South).</summary>
    public double? GpsLatitudeDecimal => GpsLatitude?.ToDecimalDegrees();

    /// <summary>Gets the GPS longitude as decimal degrees (positive = East, negative = West).</summary>
    public double? GpsLongitudeDecimal => GpsLongitude?.ToDecimalDegrees();

    /// <summary>Gets the GPS altitude in meters (negative if below sea level).</summary>
    public double? GpsAltitudeMeters
    {
        get
        {
            if (!GpsAltitude.HasValue) return null;
            var alt = GpsAltitude.Value.ToDouble();
            return GpsAltitudeRef == 1 ? -alt : alt;
        }
    }

    /// <summary>
    /// Gets a formatted exposure string (e.g., "1/250s, f/2.8, ISO 400, 50mm").
    /// </summary>
    public string? GetExposureSummary()
    {
        var parts = new System.Collections.Generic.List<string>();

        if (ExposureTime.HasValue)
            parts.Add($"{ExposureTime.Value}s");
        if (FNumber.HasValue)
            parts.Add($"f/{FNumber.Value.ToFloat():0.#}");
        if (IsoSpeedRatings.HasValue)
            parts.Add($"ISO {IsoSpeedRatings.Value}");
        if (FocalLength.HasValue)
            parts.Add($"{FocalLength.Value.ToFloat():0.#}mm");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}

/// <summary>
/// Represents a GPS coordinate (degrees, minutes, seconds) with a reference direction.
/// </summary>
public readonly struct ExifGpsCoordinate
{
    /// <summary>Degrees component.</summary>
    public ExifRational Degrees { get; }

    /// <summary>Minutes component.</summary>
    public ExifRational Minutes { get; }

    /// <summary>Seconds component.</summary>
    public ExifRational Seconds { get; }

    /// <summary>Reference direction: 'N', 'S', 'E', or 'W'.</summary>
    public char Reference { get; }

    /// <summary>
    /// Creates a GPS coordinate.
    /// </summary>
    public ExifGpsCoordinate(ExifRational degrees, ExifRational minutes, ExifRational seconds, char reference)
    {
        Degrees = degrees;
        Minutes = minutes;
        Seconds = seconds;
        Reference = reference;
    }

    /// <summary>
    /// Converts to decimal degrees (positive for N/E, negative for S/W).
    /// </summary>
    public double ToDecimalDegrees()
    {
        var dd = Degrees.ToDouble() + Minutes.ToDouble() / 60.0 + Seconds.ToDouble() / 3600.0;
        return Reference is 'S' or 'W' ? -dd : dd;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{Degrees.ToFloat():0.##}\u00b0{Minutes.ToFloat():0.##}'{Seconds.ToFloat():0.##}\"{Reference}";
    }
}
