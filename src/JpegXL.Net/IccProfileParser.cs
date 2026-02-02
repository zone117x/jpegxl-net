using System.Buffers.Binary;
using System.Text;

namespace JpegXL.Net;

/// <summary>
/// Parses ICC profile binary data to extract metadata.
/// This provides additional functionality beyond what jxl-rs exposes.
/// </summary>
public static class IccProfileParser
{
    // Tag signatures
    private const uint DescTagSignature = 0x64657363; // 'desc'
    private const uint WtptTagSignature = 0x77747074; // 'wtpt' - white point
    private const uint RXyzTagSignature = 0x7258595A; // 'rXYZ' - red primary
    private const uint GXyzTagSignature = 0x6758595A; // 'gXYZ' - green primary
    private const uint BXyzTagSignature = 0x6258595A; // 'bXYZ' - blue primary
    private const uint RTrcTagSignature = 0x72545243; // 'rTRC' - red TRC
    private const uint GTrcTagSignature = 0x67545243; // 'gTRC' - green TRC
    private const uint BTrcTagSignature = 0x62545243; // 'bTRC' - blue TRC
    private const uint KTrcTagSignature = 0x6B545243; // 'kTRC' - grayscale TRC
    private const uint CicpTagSignature = 0x63696370; // 'cicp' - CICP code points

    // Type signatures
    private const uint Mluc = 0x6D6C7563; // 'mluc' - multiLocalizedUnicodeType
    private const uint Text = 0x74657874; // 'text' - textType (ICC v2)
    private const uint Desc = 0x64657363; // 'desc' - textDescriptionType (ICC v2)
    private const uint XyzType = 0x58595A20; // 'XYZ ' - XYZ type
    private const uint CurvType = 0x63757276; // 'curv' - curve type
    private const uint ParaType = 0x70617261; // 'para' - parametric curve type
    private const uint CicpType = 0x63696370; // 'cicp' - CICP type

    // Profile class signatures
    private const uint InputClass = 0x73636E72; // 'scnr'
    private const uint DisplayClass = 0x6D6E7472; // 'mntr'
    private const uint OutputClass = 0x70727472; // 'prtr'
    private const uint DeviceLinkClass = 0x6C696E6B; // 'link'
    private const uint ColorSpaceClass = 0x73706163; // 'spac'
    private const uint AbstractClass = 0x61627374; // 'abst'
    private const uint NamedColorClass = 0x6E6D636C; // 'nmcl'

    // Color space signatures
    private const uint RgbColorSpace = 0x52474220; // 'RGB '
    private const uint GrayColorSpace = 0x47524159; // 'GRAY'
    private const uint CmykColorSpace = 0x434D594B; // 'CMYK'
    private const uint XyzColorSpace = 0x58595A20; // 'XYZ '
    private const uint LabColorSpace = 0x4C616220; // 'Lab '

    /// <summary>
    /// Attempts to extract the profile description from ICC binary data.
    /// Returns null if the data is invalid or the description cannot be found.
    /// </summary>
    public static string? TryGetDescription(ReadOnlySpan<byte> iccData)
    {
        if (!TryFindTag(iccData, DescTagSignature, out var tagData))
            return null;

        return ParseDescriptionTag(tagData);
    }

    /// <summary>
    /// Attempts to parse the ICC profile header (first 128 bytes).
    /// Returns null if the data is invalid or too short.
    /// </summary>
    public static IccHeaderInfo? TryGetHeaderInfo(ReadOnlySpan<byte> iccData)
    {
        // Minimum ICC profile size: 128 byte header
        if (iccData.Length < 128)
            return null;

        try
        {
            // Profile size at offset 0
            var profileSize = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(0, 4));

            // ICC version at offset 8 (major.minor.bugfix.0)
            var versionByte = iccData[8];
            var minorBugfix = iccData[9];
            var major = versionByte;
            var minor = (minorBugfix >> 4) & 0x0F;
            var bugfix = minorBugfix & 0x0F;
            var version = new Version(major, minor, bugfix, 0);

            // Profile class at offset 12
            var profileClassSig = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(12, 4));
            var profileClass = profileClassSig switch
            {
                InputClass => IccProfileClass.Input,
                DisplayClass => IccProfileClass.Display,
                OutputClass => IccProfileClass.Output,
                DeviceLinkClass => IccProfileClass.DeviceLink,
                ColorSpaceClass => IccProfileClass.ColorSpace,
                AbstractClass => IccProfileClass.Abstract,
                NamedColorClass => IccProfileClass.NamedColor,
                _ => IccProfileClass.Unknown
            };

            // Data color space at offset 16
            var colorSpaceSig = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(16, 4));
            var colorSpace = colorSpaceSig switch
            {
                RgbColorSpace => IccColorSpaceType.Rgb,
                GrayColorSpace => IccColorSpaceType.Gray,
                CmykColorSpace => IccColorSpaceType.Cmyk,
                XyzColorSpace => IccColorSpaceType.Xyz,
                LabColorSpace => IccColorSpaceType.Lab,
                _ => IccColorSpaceType.Unknown
            };

            // Rendering intent at offset 64 (only lower 2 bits are used)
            var intentValue = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(64, 4)) & 0x03;
            var renderingIntent = (IccRenderingIntent)intentValue;

            return new IccHeaderInfo(profileClass, colorSpace, version, renderingIntent, profileSize);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to parse color space information including primaries, white point, and transfer function.
    /// Returns null if the data is invalid.
    /// </summary>
    public static IccColorSpaceInfo? TryGetColorSpaceInfo(ReadOnlySpan<byte> iccData)
    {
        if (iccData.Length < 132)
            return null;

        try
        {
            // Extract white point
            XyzColor? whitePoint = null;
            if (TryFindTag(iccData, WtptTagSignature, out var wtptData))
            {
                whitePoint = ParseXyzTag(wtptData);
            }

            // Extract primaries
            XyzColor? redPrimary = null;
            XyzColor? greenPrimary = null;
            XyzColor? bluePrimary = null;

            if (TryFindTag(iccData, RXyzTagSignature, out var rXyzData))
            {
                redPrimary = ParseXyzTag(rXyzData);
            }
            if (TryFindTag(iccData, GXyzTagSignature, out var gXyzData))
            {
                greenPrimary = ParseXyzTag(gXyzData);
            }
            if (TryFindTag(iccData, BXyzTagSignature, out var bXyzData))
            {
                bluePrimary = ParseXyzTag(bXyzData);
            }

            // Extract transfer function from TRC tags
            // Try rTRC first (RGB profiles), then kTRC (grayscale profiles)
            IccTransferFunction? transferFunction = null;
            float? gammaValue = null;

            if (TryFindTag(iccData, RTrcTagSignature, out var trcData))
            {
                (transferFunction, gammaValue) = ParseTrcTag(trcData);
            }
            else if (TryFindTag(iccData, KTrcTagSignature, out var kTrcData))
            {
                (transferFunction, gammaValue) = ParseTrcTag(kTrcData);
            }

            // Extract CICP (Coding-Independent Code Points) if present
            // This is the most reliable way to detect HLG/PQ in ICC v4.4+ profiles
            IccCicpPrimaries? cicpPrimaries = null;
            IccCicpTransfer? cicpTransfer = null;

            if (TryFindTag(iccData, CicpTagSignature, out var cicpData))
            {
                (cicpPrimaries, cicpTransfer) = ParseCicpTag(cicpData);

                // Override transfer function based on CICP if detected
                if (cicpTransfer == IccCicpTransfer.Hlg)
                    transferFunction = IccTransferFunction.Hlg;
                else if (cicpTransfer == IccCicpTransfer.Pq)
                    transferFunction = IccTransferFunction.Pq;
                else if (cicpTransfer == IccCicpTransfer.Srgb)
                    transferFunction = IccTransferFunction.Srgb;
            }

            return new IccColorSpaceInfo(
                whitePoint,
                redPrimary,
                greenPrimary,
                bluePrimary,
                transferFunction,
                gammaValue,
                cicpPrimaries,
                cicpTransfer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to find a tag in the ICC profile and return its data.
    /// </summary>
    private static bool TryFindTag(ReadOnlySpan<byte> iccData, uint tagSignature, out ReadOnlySpan<byte> tagData)
    {
        tagData = default;

        if (iccData.Length < 132)
            return false;

        try
        {
            var tagCount = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(128, 4));

            if (tagCount > 100 || iccData.Length < 132 + tagCount * 12)
                return false;

            for (int i = 0; i < tagCount; i++)
            {
                var tagOffset = 132 + i * 12;
                var signature = BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset, 4));

                if (signature == tagSignature)
                {
                    var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset + 4, 4));
                    var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(iccData.Slice(tagOffset + 8, 4));

                    if (dataOffset + dataSize > iccData.Length || dataSize < 8)
                        return false;

                    tagData = iccData.Slice(dataOffset, dataSize);
                    return true;
                }
            }
        }
        catch
        {
            // Invalid data
        }

        return false;
    }

    /// <summary>
    /// Parses an XYZ type tag and returns the XYZ color value.
    /// </summary>
    private static XyzColor? ParseXyzTag(ReadOnlySpan<byte> tagData)
    {
        if (tagData.Length < 20)
            return null;

        var typeSignature = BinaryPrimitives.ReadUInt32BigEndian(tagData.Slice(0, 4));
        if (typeSignature != XyzType)
            return null;

        // Skip type signature (4) + reserved (4), then read 3 s15Fixed16Number values
        var x = ReadS15Fixed16(tagData.Slice(8, 4));
        var y = ReadS15Fixed16(tagData.Slice(12, 4));
        var z = ReadS15Fixed16(tagData.Slice(16, 4));

        return new XyzColor(x, y, z);
    }

    /// <summary>
    /// Parses a TRC (tone reproduction curve) tag and returns the transfer function type and gamma.
    /// </summary>
    private static (IccTransferFunction?, float?) ParseTrcTag(ReadOnlySpan<byte> tagData)
    {
        if (tagData.Length < 8)
            return (null, null);

        var typeSignature = BinaryPrimitives.ReadUInt32BigEndian(tagData.Slice(0, 4));

        if (typeSignature == CurvType)
        {
            // 'curv' type: 4 bytes signature + 4 bytes reserved + 4 bytes count + n * 2 bytes values
            if (tagData.Length < 12)
                return (IccTransferFunction.LookupTable, null);

            var count = BinaryPrimitives.ReadUInt32BigEndian(tagData.Slice(8, 4));

            if (count == 0)
            {
                // Identity curve (linear)
                return (IccTransferFunction.Linear, 1.0f);
            }
            else if (count == 1)
            {
                // Single value = gamma encoded as u8Fixed8Number
                if (tagData.Length < 14)
                    return (IccTransferFunction.Gamma, null);

                var gammaFixed = BinaryPrimitives.ReadUInt16BigEndian(tagData.Slice(12, 2));
                var gamma = gammaFixed / 256.0f;

                // Check if effectively linear
                if (Math.Abs(gamma - 1.0f) < 0.01f)
                    return (IccTransferFunction.Linear, gamma);

                return (IccTransferFunction.Gamma, gamma);
            }
            else
            {
                // Lookup table
                return (IccTransferFunction.LookupTable, null);
            }
        }
        else if (typeSignature == ParaType)
        {
            // 'para' type: parametric curve
            if (tagData.Length < 12)
                return (IccTransferFunction.Parametric, null);

            // Function type at offset 8 (2 bytes)
            var functionType = BinaryPrimitives.ReadUInt16BigEndian(tagData.Slice(8, 2));

            // Type 0 is simple gamma: Y = X^g
            if (functionType == 0 && tagData.Length >= 16)
            {
                var gamma = ReadS15Fixed16(tagData.Slice(12, 4));

                if (Math.Abs(gamma - 1.0f) < 0.01f)
                    return (IccTransferFunction.Linear, gamma);

                return (IccTransferFunction.Gamma, gamma);
            }

            // Other parametric types are more complex (sRGB-like, etc.)
            return (IccTransferFunction.Parametric, null);
        }

        return (IccTransferFunction.Unknown, null);
    }

    /// <summary>
    /// Parses a CICP (Coding-Independent Code Points) tag and returns primaries and transfer codes.
    /// </summary>
    private static (IccCicpPrimaries?, IccCicpTransfer?) ParseCicpTag(ReadOnlySpan<byte> tagData)
    {
        // CICP structure: 'cicp' (4) + reserved (4) + primaries (1) + transfer (1) + matrix (1) + range (1)
        if (tagData.Length < 12)
            return (null, null);

        var typeSignature = BinaryPrimitives.ReadUInt32BigEndian(tagData.Slice(0, 4));
        if (typeSignature != CicpType)
            return (null, null);

        var primariesByte = tagData[8];
        var transferByte = tagData[9];

        // Map to enum values, using Unknown for unrecognized codes
        var primaries = primariesByte switch
        {
            1 => IccCicpPrimaries.Bt709,
            9 => IccCicpPrimaries.Bt2020,
            12 => IccCicpPrimaries.DisplayP3,
            _ => IccCicpPrimaries.Unknown
        };

        var transfer = transferByte switch
        {
            1 => IccCicpTransfer.Bt709,
            13 => IccCicpTransfer.Srgb,
            14 => IccCicpTransfer.Bt2020_10bit,
            15 => IccCicpTransfer.Bt2020_12bit,
            16 => IccCicpTransfer.Pq,
            18 => IccCicpTransfer.Hlg,
            _ => IccCicpTransfer.Unknown
        };

        return (primaries, transfer);
    }

    /// <summary>
    /// Reads an s15Fixed16Number (signed 15.16 fixed-point) as a float.
    /// </summary>
    private static float ReadS15Fixed16(ReadOnlySpan<byte> data)
    {
        var raw = BinaryPrimitives.ReadInt32BigEndian(data);
        return raw / 65536.0f;
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
#if NETSTANDARD2_0
        return Encoding.BigEndianUnicode.GetString(data.Slice(stringOffset, stringLength).ToArray()).TrimEnd('\0');
#else
        return Encoding.BigEndianUnicode.GetString(data.Slice(stringOffset, stringLength)).TrimEnd('\0');
#endif
    }

    /// <summary>
    /// Parses textType (ICC v2).
    /// </summary>
    private static string? ParseText(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 8)
            return null;

        // Skip type signature (4) + reserved (4)
#if NETSTANDARD2_0
        return Encoding.ASCII.GetString(data.Slice(8).ToArray()).TrimEnd('\0');
#else
        return Encoding.ASCII.GetString(data.Slice(8)).TrimEnd('\0');
#endif
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

#if NETSTANDARD2_0
        return Encoding.ASCII.GetString(data.Slice(12, asciiLength).ToArray()).TrimEnd('\0');
#else
        return Encoding.ASCII.GetString(data.Slice(12, asciiLength)).TrimEnd('\0');
#endif
    }
}
