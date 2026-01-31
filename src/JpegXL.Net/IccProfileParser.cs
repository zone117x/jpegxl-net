using System.Buffers.Binary;
using System.Text;

namespace JpegXL.Net;

/// <summary>
/// Parses ICC profile binary data to extract metadata.
/// This provides additional functionality beyond what jxl-rs exposes.
/// </summary>
public static class IccProfileParser
{
    private const uint DescTagSignature = 0x64657363; // 'desc'
    private const uint Mluc = 0x6D6C7563; // 'mluc' - multiLocalizedUnicodeType
    private const uint Text = 0x74657874; // 'text' - textType (ICC v2)
    private const uint Desc = 0x64657363; // 'desc' - textDescriptionType (ICC v2)

    /// <summary>
    /// Attempts to extract the profile description from ICC binary data.
    /// Returns null if the data is invalid or the description cannot be found.
    /// </summary>
    public static string? TryGetDescription(ReadOnlySpan<byte> iccData)
    {
        // Minimum ICC profile size: 128 byte header + 4 byte tag count
        if (iccData.Length < 132)
            return null;

        try
        {
            // Tag count at offset 128 (big-endian)
            var tagCount = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(128, 4));

            // Sanity check
            if (tagCount > 100 || iccData.Length < 132 + tagCount * 12)
                return null;

            // Search for 'desc' tag in tag table (starts at offset 132)
            for (int i = 0; i < tagCount; i++)
            {
                var tagOffset = 132 + i * 12;
                var signature = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset, 4));

                if (signature == DescTagSignature)
                {
                    var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset + 4, 4));
                    var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset + 8, 4));

                    if (dataOffset + dataSize > iccData.Length || dataSize < 8)
                        return null;

                    return ParseDescriptionTag(iccData.Slice(dataOffset, dataSize));
                }
            }
        }
        catch
        {
            // Invalid data - return null
        }

        return null;
    }

    private static string? ParseDescriptionTag(ReadOnlySpan<byte> tagData)
    {
        if (tagData.Length < 8)
            return null;

        var typeSignature = BinaryPrimitives.ReadUInt32BigEndian(tagData.Slice(0, 4));

        return typeSignature switch
        {
            Mluc => ParseMluc(tagData),
            Text => ParseText(tagData),
            Desc => ParseTextDescription(tagData),
            _ => null
        };
    }

    /// <summary>
    /// Parses multiLocalizedUnicodeType (ICC v4).
    /// </summary>
    private static string? ParseMluc(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return null;

        var recordCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));
        if (recordCount == 0 || data.Length < 16 + recordCount * 12)
            return null;

        // Use first record (typically 'enUS')
        var stringLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        var stringOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(24, 4));

        if (stringOffset + stringLength > data.Length || stringLength == 0)
            return null;

        // mluc strings are UTF-16BE
        return Encoding.BigEndianUnicode.GetString(data.Slice(stringOffset, stringLength)).TrimEnd('\0');
    }

    /// <summary>
    /// Parses textType (ICC v2).
    /// </summary>
    private static string? ParseText(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 8)
            return null;

        // Skip type signature (4) + reserved (4)
        return Encoding.ASCII.GetString(data.Slice(8)).TrimEnd('\0');
    }

    /// <summary>
    /// Parses textDescriptionType (ICC v2).
    /// </summary>
    private static string? ParseTextDescription(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return null;

        // Skip type signature (4) + reserved (4)
        var asciiLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));

        if (asciiLength == 0 || 12 + asciiLength > data.Length)
            return null;

        return Encoding.ASCII.GetString(data.Slice(12, asciiLength)).TrimEnd('\0');
    }
}
