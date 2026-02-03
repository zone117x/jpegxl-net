using System;
using System.Buffers.Binary;
using System.Text;

namespace JpegXL.Net;

/// <summary>
/// Parses EXIF binary data to extract metadata.
/// </summary>
/// <remarks>
/// EXIF data from JPEG XL files typically starts with a 4-byte TIFF offset followed by TIFF data.
/// The TIFF structure contains IFD (Image File Directory) entries with tag/value pairs.
/// </remarks>
public static class ExifDataParser
{
    // TIFF byte order markers
    private const ushort LittleEndianMarker = 0x4949; // "II"
    private const ushort BigEndianMarker = 0x4D4D;    // "MM"
    private const ushort TiffMagicNumber = 42;

    // IFD0 (main image) tags
    private const ushort TagImageWidth = 0x0100;
    private const ushort TagImageHeight = 0x0101;
    private const ushort TagImageDescription = 0x010E;
    private const ushort TagMake = 0x010F;
    private const ushort TagModel = 0x0110;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagSoftware = 0x0131;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagArtist = 0x013B;
    private const ushort TagCopyright = 0x8298;
    private const ushort TagExifIfdPointer = 0x8769;
    private const ushort TagGpsIfdPointer = 0x8825;

    // EXIF SubIFD tags
    private const ushort TagExposureTime = 0x829A;
    private const ushort TagFNumber = 0x829D;
    private const ushort TagExposureProgram = 0x8822;
    private const ushort TagIsoSpeedRatings = 0x8827;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagDateTimeDigitized = 0x9004;
    private const ushort TagExposureBias = 0x9204;
    private const ushort TagMeteringMode = 0x9207;
    private const ushort TagFlash = 0x9209;
    private const ushort TagFocalLength = 0x920A;
    private const ushort TagFocalLengthIn35mmFilm = 0xA405;

    // GPS SubIFD tags
    private const ushort TagGpsLatitudeRef = 0x0001;
    private const ushort TagGpsLatitude = 0x0002;
    private const ushort TagGpsLongitudeRef = 0x0003;
    private const ushort TagGpsLongitude = 0x0004;
    private const ushort TagGpsAltitudeRef = 0x0005;
    private const ushort TagGpsAltitude = 0x0006;

    // IFD1 (thumbnail) tags
    private const ushort TagThumbnailOffset = 0x0201;
    private const ushort TagThumbnailLength = 0x0202;

    // TIFF data types
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TypeUndefined = 7;
    private const ushort TypeSLong = 9;
    private const ushort TypeSRational = 10;

    /// <summary>
    /// Attempts to parse all EXIF data from raw bytes.
    /// </summary>
    /// <param name="exifData">Raw EXIF data bytes (from JXL container).</param>
    /// <returns>Parsed EXIF data, or null if parsing fails.</returns>
    public static ExifData? TryParse(ReadOnlySpan<byte> exifData)
    {
        if (!TryParseHeader(exifData, out var isLittleEndian, out var ifd0Offset, out var tiffBase))
            return null;

        var tiffData = exifData.Slice(tiffBase);

        // Parse IFD0 (main image directory)
        if (!TryParseIfd(tiffData, ifd0Offset, isLittleEndian, out var ifd0Entries, out var ifd1Offset))
            return null;

        var data = new ExifData
        {
            // Basic info from IFD0
            Make = GetString(tiffData, ifd0Entries, TagMake, isLittleEndian),
            Model = GetString(tiffData, ifd0Entries, TagModel, isLittleEndian),
            Software = GetString(tiffData, ifd0Entries, TagSoftware, isLittleEndian),
            ImageDescription = GetString(tiffData, ifd0Entries, TagImageDescription, isLittleEndian),
            Copyright = GetString(tiffData, ifd0Entries, TagCopyright, isLittleEndian),
            Artist = GetString(tiffData, ifd0Entries, TagArtist, isLittleEndian),
            DateTime = ParseExifDateTime(GetString(tiffData, ifd0Entries, TagDateTime, isLittleEndian)),
            Orientation = GetOrientation(ifd0Entries, isLittleEndian),
            ImageWidth = GetUInt32(ifd0Entries, TagImageWidth, isLittleEndian),
            ImageHeight = GetUInt32(ifd0Entries, TagImageHeight, isLittleEndian),
        };

        // Parse EXIF SubIFD if present
        if (TryGetUInt32(ifd0Entries, TagExifIfdPointer, isLittleEndian, out var exifIfdOffset))
        {
            if (TryParseIfd(tiffData, (int)exifIfdOffset, isLittleEndian, out var exifEntries, out _))
            {
                data = data with
                {
                    ExposureTime = GetRational(tiffData, exifEntries, TagExposureTime, isLittleEndian),
                    FNumber = GetRational(tiffData, exifEntries, TagFNumber, isLittleEndian),
                    ExposureProgram = GetUInt16(exifEntries, TagExposureProgram, isLittleEndian),
                    IsoSpeedRatings = GetUInt16(exifEntries, TagIsoSpeedRatings, isLittleEndian),
                    DateTimeOriginal = ParseExifDateTime(GetString(tiffData, exifEntries, TagDateTimeOriginal, isLittleEndian)),
                    DateTimeDigitized = ParseExifDateTime(GetString(tiffData, exifEntries, TagDateTimeDigitized, isLittleEndian)),
                    ExposureBias = GetSignedRational(tiffData, exifEntries, TagExposureBias, isLittleEndian),
                    MeteringMode = GetUInt16(exifEntries, TagMeteringMode, isLittleEndian),
                    Flash = GetUInt16(exifEntries, TagFlash, isLittleEndian),
                    FocalLength = GetRational(tiffData, exifEntries, TagFocalLength, isLittleEndian),
                    FocalLengthIn35mmFilm = GetUInt16(exifEntries, TagFocalLengthIn35mmFilm, isLittleEndian),
                };
            }
        }

        // Parse GPS SubIFD if present
        if (TryGetUInt32(ifd0Entries, TagGpsIfdPointer, isLittleEndian, out var gpsIfdOffset))
        {
            if (TryParseIfd(tiffData, (int)gpsIfdOffset, isLittleEndian, out var gpsEntries, out _))
            {
                var latRef = GetString(tiffData, gpsEntries, TagGpsLatitudeRef, isLittleEndian);
                var lonRef = GetString(tiffData, gpsEntries, TagGpsLongitudeRef, isLittleEndian);

                data = data with
                {
                    GpsLatitude = GetGpsCoordinate(tiffData, gpsEntries, TagGpsLatitude, isLittleEndian, latRef),
                    GpsLongitude = GetGpsCoordinate(tiffData, gpsEntries, TagGpsLongitude, isLittleEndian, lonRef),
                    GpsAltitude = GetRational(tiffData, gpsEntries, TagGpsAltitude, isLittleEndian),
                    GpsAltitudeRef = GetByte(gpsEntries, TagGpsAltitudeRef),
                };
            }
        }

        // Parse IFD1 (thumbnail) if present
        if (ifd1Offset > 0 && TryParseIfd(tiffData, ifd1Offset, isLittleEndian, out var ifd1Entries, out _))
        {
            var thumbOffset = GetUInt32(ifd1Entries, TagThumbnailOffset, isLittleEndian);
            var thumbLength = GetUInt32(ifd1Entries, TagThumbnailLength, isLittleEndian);

            if (thumbOffset.HasValue && thumbLength.HasValue && thumbLength.Value > 0)
            {
                data = data with
                {
                    HasThumbnail = true,
                    ThumbnailOffset = tiffBase + (int)thumbOffset.Value,
                    ThumbnailLength = (int)thumbLength.Value,
                };
            }
        }

        return data;
    }

    /// <summary>
    /// Attempts to get the camera make and model.
    /// </summary>
    public static bool TryGetMakeModel(ReadOnlySpan<byte> exifData, out string? make, out string? model)
    {
        make = null;
        model = null;

        if (!TryParseHeader(exifData, out var isLittleEndian, out var ifd0Offset, out var tiffBase))
            return false;

        var tiffData = exifData.Slice(tiffBase);

        if (!TryParseIfd(tiffData, ifd0Offset, isLittleEndian, out var entries, out _))
            return false;

        make = GetString(tiffData, entries, TagMake, isLittleEndian);
        model = GetString(tiffData, entries, TagModel, isLittleEndian);

        return make != null || model != null;
    }

    /// <summary>
    /// Attempts to get the date/time the image was taken.
    /// </summary>
    public static bool TryGetDateTime(ReadOnlySpan<byte> exifData, out DateTime? dateTime)
    {
        dateTime = null;

        if (!TryParseHeader(exifData, out var isLittleEndian, out var ifd0Offset, out var tiffBase))
            return false;

        var tiffData = exifData.Slice(tiffBase);

        if (!TryParseIfd(tiffData, ifd0Offset, isLittleEndian, out var ifd0Entries, out _))
            return false;

        // Try EXIF SubIFD first for DateTimeOriginal
        if (TryGetUInt32(ifd0Entries, TagExifIfdPointer, isLittleEndian, out var exifIfdOffset))
        {
            if (TryParseIfd(tiffData, (int)exifIfdOffset, isLittleEndian, out var exifEntries, out _))
            {
                var dtOriginal = GetString(tiffData, exifEntries, TagDateTimeOriginal, isLittleEndian);
                dateTime = ParseExifDateTime(dtOriginal);
                if (dateTime.HasValue)
                    return true;
            }
        }

        // Fall back to IFD0 DateTime
        var dt = GetString(tiffData, ifd0Entries, TagDateTime, isLittleEndian);
        dateTime = ParseExifDateTime(dt);
        return dateTime.HasValue;
    }

    /// <summary>
    /// Attempts to get the image orientation.
    /// </summary>
    public static bool TryGetOrientation(ReadOnlySpan<byte> exifData, out JxlOrientation? orientation)
    {
        orientation = null;

        if (!TryParseHeader(exifData, out var isLittleEndian, out var ifd0Offset, out var tiffBase))
            return false;

        var tiffData = exifData.Slice(tiffBase);

        if (!TryParseIfd(tiffData, ifd0Offset, isLittleEndian, out var entries, out _))
            return false;

        orientation = GetOrientation(entries, isLittleEndian);
        return orientation.HasValue;
    }

    /// <summary>
    /// Attempts to extract the embedded thumbnail JPEG data.
    /// </summary>
    /// <param name="exifData">Raw EXIF data bytes.</param>
    /// <param name="thumbnail">The thumbnail JPEG data if found.</param>
    /// <returns>True if a thumbnail was found and extracted.</returns>
    public static bool TryGetThumbnail(ReadOnlySpan<byte> exifData, out ReadOnlySpan<byte> thumbnail)
    {
        thumbnail = default;

        if (!TryParseHeader(exifData, out var isLittleEndian, out var ifd0Offset, out var tiffBase))
            return false;

        var tiffData = exifData.Slice(tiffBase);

        if (!TryParseIfd(tiffData, ifd0Offset, isLittleEndian, out _, out var ifd1Offset))
            return false;

        if (ifd1Offset <= 0)
            return false;

        if (!TryParseIfd(tiffData, ifd1Offset, isLittleEndian, out var ifd1Entries, out _))
            return false;

        var thumbOffset = GetUInt32(ifd1Entries, TagThumbnailOffset, isLittleEndian);
        var thumbLength = GetUInt32(ifd1Entries, TagThumbnailLength, isLittleEndian);

        if (!thumbOffset.HasValue || !thumbLength.HasValue || thumbLength.Value == 0)
            return false;

        var offset = (int)thumbOffset.Value;
        var length = (int)thumbLength.Value;

        if (offset < 0 || offset + length > tiffData.Length)
            return false;

        thumbnail = tiffData.Slice(offset, length);
        return true;
    }

    // ========================================================================
    // Private parsing methods
    // ========================================================================

    /// <summary>
    /// Parses the EXIF/TIFF header and returns byte order and IFD0 offset.
    /// </summary>
    private static bool TryParseHeader(ReadOnlySpan<byte> data, out bool isLittleEndian, out int ifd0Offset, out int tiffBase)
    {
        isLittleEndian = false;
        ifd0Offset = 0;
        tiffBase = 0;

        // EXIF from JXL has a 4-byte TIFF offset prefix
        if (data.Length < 12)
            return false;

        // The first 4 bytes are the TIFF offset (usually 0)
        tiffBase = 4;

        // Check for "Exif\0\0" header (some formats include this)
        if (data.Length >= 10 && data[4] == 'E' && data[5] == 'x' && data[6] == 'i' && data[7] == 'f')
        {
            tiffBase = 10; // Skip "Exif\0\0"
        }

        var tiffData = data.Slice(tiffBase);
        if (tiffData.Length < 8)
            return false;

        // Read byte order marker
        var byteOrder = BinaryPrimitives.ReadUInt16LittleEndian(tiffData);
        if (byteOrder == LittleEndianMarker)
        {
            isLittleEndian = true;
        }
        else if (byteOrder == BigEndianMarker)
        {
            isLittleEndian = false;
        }
        else
        {
            return false;
        }

        // Verify TIFF magic number (42)
        var magic = ReadUInt16(tiffData.Slice(2), isLittleEndian);
        if (magic != TiffMagicNumber)
            return false;

        // Read IFD0 offset
        ifd0Offset = (int)ReadUInt32(tiffData.Slice(4), isLittleEndian);

        return ifd0Offset >= 8 && ifd0Offset < tiffData.Length;
    }

    /// <summary>
    /// Parses an IFD (Image File Directory) and returns its entries.
    /// </summary>
    private static bool TryParseIfd(ReadOnlySpan<byte> tiffData, int offset, bool isLittleEndian,
        out IfdEntry[] entries, out int nextIfdOffset)
    {
        entries = Array.Empty<IfdEntry>();
        nextIfdOffset = 0;

        if (offset < 0 || offset + 2 > tiffData.Length)
            return false;

        var entryCount = ReadUInt16(tiffData.Slice(offset), isLittleEndian);

        if (entryCount > 500 || offset + 2 + entryCount * 12 + 4 > tiffData.Length)
            return false;

        entries = new IfdEntry[entryCount];

        for (int i = 0; i < entryCount; i++)
        {
            var entryOffset = offset + 2 + i * 12;
            var entryData = tiffData.Slice(entryOffset, 12);

            entries[i] = new IfdEntry
            {
                Tag = ReadUInt16(entryData, isLittleEndian),
                Type = ReadUInt16(entryData.Slice(2), isLittleEndian),
                Count = ReadUInt32(entryData.Slice(4), isLittleEndian),
                ValueOffset = entryData.Slice(8, 4).ToArray()
            };
        }

        // Read next IFD offset
        var nextOffset = offset + 2 + entryCount * 12;
        if (nextOffset + 4 <= tiffData.Length)
        {
            nextIfdOffset = (int)ReadUInt32(tiffData.Slice(nextOffset), isLittleEndian);
        }

        return true;
    }

    // ========================================================================
    // Value extraction helpers
    // ========================================================================

    private static string? GetString(ReadOnlySpan<byte> tiffData, IfdEntry[] entries, ushort tag, bool isLittleEndian)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeAscii && entry.Count > 0)
            {
                ReadOnlySpan<byte> strData;
                var length = (int)entry.Count;

                if (length <= 4)
                {
                    // Value is inline
                    strData = entry.ValueOffset.AsSpan(0, length);
                }
                else
                {
                    // Value is at offset
                    var offset = (int)ReadUInt32(entry.ValueOffset, isLittleEndian);
                    if (offset < 0 || offset + length > tiffData.Length)
                        return null;
                    strData = tiffData.Slice(offset, length);
                }

                // Remove null terminator and trailing whitespace
#if NETSTANDARD2_0
                return Encoding.ASCII.GetString(strData.ToArray()).TrimEnd('\0', ' ');
#else
                return Encoding.ASCII.GetString(strData).TrimEnd('\0', ' ');
#endif
            }
        }
        return null;
    }

    private static ushort? GetUInt16(IfdEntry[] entries, ushort tag, bool isLittleEndian)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeShort && entry.Count >= 1)
            {
                return ReadUInt16(entry.ValueOffset, isLittleEndian);
            }
        }
        return null;
    }

    private static uint? GetUInt32(IfdEntry[] entries, ushort tag, bool isLittleEndian)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag)
            {
                if (entry.Type == TypeLong && entry.Count >= 1)
                {
                    return ReadUInt32(entry.ValueOffset, isLittleEndian);
                }
                else if (entry.Type == TypeShort && entry.Count >= 1)
                {
                    return ReadUInt16(entry.ValueOffset, isLittleEndian);
                }
            }
        }
        return null;
    }

    private static bool TryGetUInt32(IfdEntry[] entries, ushort tag, bool isLittleEndian, out uint value)
    {
        var result = GetUInt32(entries, tag, isLittleEndian);
        value = result ?? 0;
        return result.HasValue;
    }

    private static byte? GetByte(IfdEntry[] entries, ushort tag)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeByte && entry.Count >= 1)
            {
                return entry.ValueOffset[0];
            }
        }
        return null;
    }

    private static ExifRational? GetRational(ReadOnlySpan<byte> tiffData, IfdEntry[] entries, ushort tag, bool isLittleEndian)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeRational && entry.Count >= 1)
            {
                var offset = (int)ReadUInt32(entry.ValueOffset, isLittleEndian);
                if (offset < 0 || offset + 8 > tiffData.Length)
                    return null;

                var num = ReadUInt32(tiffData.Slice(offset), isLittleEndian);
                var den = ReadUInt32(tiffData.Slice(offset + 4), isLittleEndian);
                return new ExifRational(num, den);
            }
        }
        return null;
    }

    private static ExifSignedRational? GetSignedRational(ReadOnlySpan<byte> tiffData, IfdEntry[] entries, ushort tag, bool isLittleEndian)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeSRational && entry.Count >= 1)
            {
                var offset = (int)ReadUInt32(entry.ValueOffset, isLittleEndian);
                if (offset < 0 || offset + 8 > tiffData.Length)
                    return null;

                var num = ReadInt32(tiffData.Slice(offset), isLittleEndian);
                var den = ReadInt32(tiffData.Slice(offset + 4), isLittleEndian);
                return new ExifSignedRational(num, den);
            }
        }
        return null;
    }

    private static JxlOrientation? GetOrientation(IfdEntry[] entries, bool isLittleEndian)
    {
        var value = GetUInt16(entries, TagOrientation, isLittleEndian);
        if (!value.HasValue || value.Value < 1 || value.Value > 8)
            return null;

        return (JxlOrientation)value.Value;
    }

    private static ExifGpsCoordinate? GetGpsCoordinate(ReadOnlySpan<byte> tiffData, IfdEntry[] entries,
        ushort tag, bool isLittleEndian, string? refStr)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag == tag && entry.Type == TypeRational && entry.Count >= 3)
            {
                var offset = (int)ReadUInt32(entry.ValueOffset, isLittleEndian);
                if (offset < 0 || offset + 24 > tiffData.Length)
                    return null;

                var degrees = new ExifRational(
                    ReadUInt32(tiffData.Slice(offset), isLittleEndian),
                    ReadUInt32(tiffData.Slice(offset + 4), isLittleEndian));
                var minutes = new ExifRational(
                    ReadUInt32(tiffData.Slice(offset + 8), isLittleEndian),
                    ReadUInt32(tiffData.Slice(offset + 12), isLittleEndian));
                var seconds = new ExifRational(
                    ReadUInt32(tiffData.Slice(offset + 16), isLittleEndian),
                    ReadUInt32(tiffData.Slice(offset + 20), isLittleEndian));

                var refChar = string.IsNullOrEmpty(refStr) ? 'N' : refStr![0];
                return new ExifGpsCoordinate(degrees, minutes, seconds, refChar);
            }
        }
        return null;
    }

    /// <summary>
    /// Parses EXIF date format "YYYY:MM:DD HH:MM:SS" to DateTime.
    /// </summary>
    private static DateTime? ParseExifDateTime(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr) || dateStr!.Length < 19)
            return null;

        // Format: "YYYY:MM:DD HH:MM:SS"
        if (DateTime.TryParseExact(dateStr.Substring(0, 19),
            "yyyy:MM:dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var result))
        {
            return result;
        }

        return null;
    }

    // ========================================================================
    // Binary reading helpers
    // ========================================================================

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool isLittleEndian)
    {
        return isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool isLittleEndian)
    {
        return isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, bool isLittleEndian)
    {
        return isLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(data)
            : BinaryPrimitives.ReadInt32BigEndian(data);
    }

    /// <summary>
    /// Represents a single IFD entry (12 bytes in TIFF).
    /// </summary>
    private struct IfdEntry
    {
        public ushort Tag;
        public ushort Type;
        public uint Count;
        public byte[] ValueOffset; // 4 bytes: either value or offset to value
    }
}
