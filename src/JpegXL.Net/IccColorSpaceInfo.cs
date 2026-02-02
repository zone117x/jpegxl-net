namespace JpegXL.Net;

/// <summary>
/// Transfer function type detected from ICC TRC curves or CICP tag.
/// </summary>
public enum IccTransferFunction
{
    /// <summary>Unknown or complex transfer function.</summary>
    Unknown = 0,
    /// <summary>Linear transfer function (gamma = 1.0).</summary>
    Linear,
    /// <summary>Simple gamma curve.</summary>
    Gamma,
    /// <summary>Parametric curve (ICC v4).</summary>
    Parametric,
    /// <summary>Lookup table curve.</summary>
    LookupTable,
    /// <summary>sRGB transfer function.</summary>
    Srgb,
    /// <summary>PQ (Perceptual Quantizer) HDR transfer function (SMPTE ST 2084).</summary>
    Pq,
    /// <summary>HLG (Hybrid Log-Gamma) HDR transfer function (ARIB STD-B67).</summary>
    Hlg
}

/// <summary>
/// CICP (Coding-Independent Code Points) primaries from ICC cicp tag.
/// </summary>
public enum IccCicpPrimaries : byte
{
    /// <summary>Unknown primaries.</summary>
    Unknown = 0,
    /// <summary>BT.709 / sRGB primaries.</summary>
    Bt709 = 1,
    /// <summary>BT.2020 / BT.2100 primaries (used for HDR).</summary>
    Bt2020 = 9,
    /// <summary>Display P3 primaries.</summary>
    DisplayP3 = 12
}

/// <summary>
/// CICP (Coding-Independent Code Points) transfer characteristics from ICC cicp tag.
/// </summary>
public enum IccCicpTransfer : byte
{
    /// <summary>Unknown transfer function.</summary>
    Unknown = 0,
    /// <summary>BT.709 transfer function.</summary>
    Bt709 = 1,
    /// <summary>sRGB transfer function.</summary>
    Srgb = 13,
    /// <summary>BT.2020 10-bit transfer function.</summary>
    Bt2020_10bit = 14,
    /// <summary>BT.2020 12-bit transfer function.</summary>
    Bt2020_12bit = 15,
    /// <summary>PQ (Perceptual Quantizer) - SMPTE ST 2084.</summary>
    Pq = 16,
    /// <summary>HLG (Hybrid Log-Gamma) - ARIB STD-B67.</summary>
    Hlg = 18
}

/// <summary>
/// CIE XYZ color value.
/// </summary>
public readonly struct XyzColor
{
    /// <summary>X component.</summary>
    public float X { get; }
    /// <summary>Y component.</summary>
    public float Y { get; }
    /// <summary>Z component.</summary>
    public float Z { get; }

    /// <summary>
    /// Creates a new XYZ color value.
    /// </summary>
    public XyzColor(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <inheritdoc/>
    public override string ToString() => $"XYZ({X:F4}, {Y:F4}, {Z:F4})";

    /// <summary>
    /// Checks if this XYZ value is approximately equal to another within the given tolerance.
    /// </summary>
    public bool ApproximatelyEquals(XyzColor other, float tolerance = 0.002f)
    {
        return Math.Abs(X - other.X) <= tolerance &&
               Math.Abs(Y - other.Y) <= tolerance &&
               Math.Abs(Z - other.Z) <= tolerance;
    }
}

/// <summary>
/// Parsed ICC color space information including primaries, white point, and transfer function.
/// </summary>
public readonly struct IccColorSpaceInfo
{
    // Standard white points (D65 is most common for sRGB, Display P3, Rec.2020)
    private static readonly XyzColor D65WhitePoint = new(0.9505f, 1.0000f, 1.0890f);
    private static readonly XyzColor D50WhitePoint = new(0.9642f, 1.0000f, 0.8249f);

    // Standard sRGB primaries (IEC 61966-2-1)
    private static readonly XyzColor SrgbRedPrimary = new(0.4360f, 0.2225f, 0.0139f);
    private static readonly XyzColor SrgbGreenPrimary = new(0.3851f, 0.7169f, 0.0971f);
    private static readonly XyzColor SrgbBluePrimary = new(0.1431f, 0.0606f, 0.7141f);

    // Display P3 primaries
    private static readonly XyzColor P3RedPrimary = new(0.4866f, 0.2289f, 0.0000f);
    private static readonly XyzColor P3GreenPrimary = new(0.2657f, 0.6917f, 0.0451f);
    private static readonly XyzColor P3BluePrimary = new(0.1982f, 0.0793f, 1.0439f);

    // Rec.2020 primaries
    private static readonly XyzColor Rec2020RedPrimary = new(0.6370f, 0.2627f, 0.0000f);
    private static readonly XyzColor Rec2020GreenPrimary = new(0.1446f, 0.6780f, 0.0281f);
    private static readonly XyzColor Rec2020BluePrimary = new(0.1689f, 0.0593f, 1.0610f);

    /// <summary>
    /// White point in CIE XYZ, or null if not available.
    /// </summary>
    public XyzColor? WhitePoint { get; }

    /// <summary>
    /// Red primary in CIE XYZ, or null if not available.
    /// </summary>
    public XyzColor? RedPrimary { get; }

    /// <summary>
    /// Green primary in CIE XYZ, or null if not available.
    /// </summary>
    public XyzColor? GreenPrimary { get; }

    /// <summary>
    /// Blue primary in CIE XYZ, or null if not available.
    /// </summary>
    public XyzColor? BluePrimary { get; }

    /// <summary>
    /// Detected transfer function type, or null if TRC tags not found.
    /// </summary>
    public IccTransferFunction? TransferFunction { get; }

    /// <summary>
    /// Gamma value if transfer function is a simple gamma curve, otherwise null.
    /// </summary>
    public float? GammaValue { get; }

    /// <summary>
    /// CICP primaries code point, if cicp tag is present.
    /// </summary>
    public IccCicpPrimaries? CicpPrimaries { get; }

    /// <summary>
    /// CICP transfer characteristics code point, if cicp tag is present.
    /// </summary>
    public IccCicpTransfer? CicpTransfer { get; }

    /// <summary>
    /// Whether the profile uses HLG (Hybrid Log-Gamma) transfer function.
    /// Detected via CICP tag or transfer function analysis.
    /// </summary>
    public bool IsHlg => TransferFunction == IccTransferFunction.Hlg || CicpTransfer == IccCicpTransfer.Hlg;

    /// <summary>
    /// Whether the profile uses PQ (Perceptual Quantizer) transfer function.
    /// Detected via CICP tag or transfer function analysis.
    /// </summary>
    public bool IsPq => TransferFunction == IccTransferFunction.Pq || CicpTransfer == IccCicpTransfer.Pq;

    /// <summary>
    /// Whether the profile is HDR (uses PQ or HLG transfer function).
    /// </summary>
    public bool IsHdr => IsHlg || IsPq;

    /// <summary>
    /// Whether the profile appears to use sRGB primaries and D65 white point.
    /// </summary>
    public bool IsLikelySrgb =>
        WhitePoint?.ApproximatelyEquals(D65WhitePoint) == true &&
        RedPrimary?.ApproximatelyEquals(SrgbRedPrimary) == true &&
        GreenPrimary?.ApproximatelyEquals(SrgbGreenPrimary) == true &&
        BluePrimary?.ApproximatelyEquals(SrgbBluePrimary) == true;

    /// <summary>
    /// Whether the profile appears to use Display P3 primaries and D65 white point.
    /// </summary>
    public bool IsLikelyDisplayP3 =>
        WhitePoint?.ApproximatelyEquals(D65WhitePoint) == true &&
        RedPrimary?.ApproximatelyEquals(P3RedPrimary) == true &&
        GreenPrimary?.ApproximatelyEquals(P3GreenPrimary) == true &&
        BluePrimary?.ApproximatelyEquals(P3BluePrimary) == true;

    /// <summary>
    /// Whether the profile appears to use Rec.2020 primaries and D65 white point.
    /// </summary>
    public bool IsLikelyRec2020 =>
        WhitePoint?.ApproximatelyEquals(D65WhitePoint) == true &&
        RedPrimary?.ApproximatelyEquals(Rec2020RedPrimary) == true &&
        GreenPrimary?.ApproximatelyEquals(Rec2020GreenPrimary) == true &&
        BluePrimary?.ApproximatelyEquals(Rec2020BluePrimary) == true;

    /// <summary>
    /// Whether the profile appears to use a linear transfer function.
    /// </summary>
    public bool IsLikelyLinear =>
        TransferFunction == IccTransferFunction.Linear ||
        (TransferFunction == IccTransferFunction.Gamma && GammaValue.HasValue && Math.Abs(GammaValue.Value - 1.0f) < 0.01f);

    /// <summary>
    /// Whether the white point appears to be D65.
    /// </summary>
    public bool IsD65WhitePoint => WhitePoint?.ApproximatelyEquals(D65WhitePoint) == true;

    /// <summary>
    /// Whether the white point appears to be D50.
    /// </summary>
    public bool IsD50WhitePoint => WhitePoint?.ApproximatelyEquals(D50WhitePoint) == true;

    internal IccColorSpaceInfo(
        XyzColor? whitePoint,
        XyzColor? redPrimary,
        XyzColor? greenPrimary,
        XyzColor? bluePrimary,
        IccTransferFunction? transferFunction,
        float? gammaValue,
        IccCicpPrimaries? cicpPrimaries = null,
        IccCicpTransfer? cicpTransfer = null)
    {
        WhitePoint = whitePoint;
        RedPrimary = redPrimary;
        GreenPrimary = greenPrimary;
        BluePrimary = bluePrimary;
        TransferFunction = transferFunction;
        GammaValue = gammaValue;
        CicpPrimaries = cicpPrimaries;
        CicpTransfer = cicpTransfer;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        // Check CICP-based detection first (more reliable for HDR)
        if (IsHlg)
        {
            return CicpPrimaries == IccCicpPrimaries.Bt2020 ? "Rec.2100 HLG" : "HLG";
        }
        if (IsPq)
        {
            return CicpPrimaries == IccCicpPrimaries.Bt2020 ? "Rec.2100 PQ" : "PQ";
        }
        if (IsLikelySrgb) return "sRGB";
        if (IsLikelyDisplayP3) return "Display P3";
        if (IsLikelyRec2020) return "Rec.2020";
        return "Custom";
    }
}
