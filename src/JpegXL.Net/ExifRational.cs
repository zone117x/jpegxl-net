namespace JpegXL.Net;

/// <summary>
/// Represents an unsigned rational number from EXIF data (numerator/denominator).
/// </summary>
public readonly struct ExifRational
{
    /// <summary>The numerator of the rational.</summary>
    public uint Numerator { get; }

    /// <summary>The denominator of the rational.</summary>
    public uint Denominator { get; }

    /// <summary>
    /// Creates a new EXIF rational.
    /// </summary>
    public ExifRational(uint numerator, uint denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>
    /// Converts the rational to a float value.
    /// </summary>
    public float ToFloat() => Denominator == 0 ? 0 : (float)Numerator / Denominator;

    /// <summary>
    /// Converts the rational to a double value.
    /// </summary>
    public double ToDouble() => Denominator == 0 ? 0 : (double)Numerator / Denominator;

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Denominator == 0) return "0";
        if (Denominator == 1) return Numerator.ToString();
        // For shutter speeds like 1/250
        if (Numerator == 1) return $"1/{Denominator}";
        return $"{Numerator}/{Denominator}";
    }
}

/// <summary>
/// Represents a signed rational number from EXIF data.
/// </summary>
public readonly struct ExifSignedRational
{
    /// <summary>The numerator of the rational.</summary>
    public int Numerator { get; }

    /// <summary>The denominator of the rational.</summary>
    public int Denominator { get; }

    /// <summary>
    /// Creates a new signed EXIF rational.
    /// </summary>
    public ExifSignedRational(int numerator, int denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>
    /// Converts the rational to a float value.
    /// </summary>
    public float ToFloat() => Denominator == 0 ? 0 : (float)Numerator / Denominator;

    /// <summary>
    /// Converts the rational to a double value.
    /// </summary>
    public double ToDouble() => Denominator == 0 ? 0 : (double)Numerator / Denominator;

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Denominator == 0) return "0";
        if (Denominator == 1) return Numerator.ToString();
        return $"{Numerator}/{Denominator}";
    }
}
