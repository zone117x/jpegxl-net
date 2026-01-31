// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Runtime.InteropServices;
using System.Text;

namespace JpegXL.Net;

/// <summary>
/// The type of color profile.
/// </summary>
public enum JxlProfileType
{
    /// <summary>ICC profile (raw bytes).</summary>
    Icc,
    /// <summary>RGB color space.</summary>
    Rgb,
    /// <summary>Grayscale color space.</summary>
    Grayscale,
    /// <summary>XYB color space (JPEG XL internal representation).</summary>
    Xyb
}

/// <summary>
/// White point specification.
/// </summary>
public enum JxlWhitePointType
{
    /// <summary>D65 standard illuminant (daylight).</summary>
    D65,
    /// <summary>Equal energy illuminant.</summary>
    E,
    /// <summary>DCI-P3 theater white point.</summary>
    Dci,
    /// <summary>Custom chromaticity coordinates.</summary>
    Custom
}

/// <summary>
/// Color primaries specification.
/// </summary>
public enum JxlPrimariesType
{
    /// <summary>sRGB/Rec.709 primaries.</summary>
    Srgb,
    /// <summary>BT.2100/Rec.2020 primaries (wide gamut for HDR).</summary>
    Bt2100,
    /// <summary>DCI-P3 primaries.</summary>
    P3,
    /// <summary>Custom chromaticity coordinates.</summary>
    Custom
}

/// <summary>
/// Transfer function (gamma curve) specification.
/// </summary>
public enum JxlTransferFunctionType
{
    /// <summary>BT.709 transfer function.</summary>
    Bt709,
    /// <summary>Linear transfer function (gamma 1.0).</summary>
    Linear,
    /// <summary>sRGB transfer function.</summary>
    Srgb,
    /// <summary>Perceptual Quantizer (PQ) for HDR content.</summary>
    Pq,
    /// <summary>DCI gamma (~2.6).</summary>
    Dci,
    /// <summary>Hybrid Log-Gamma (HLG) for HDR broadcast.</summary>
    Hlg,
    /// <summary>Custom gamma value.</summary>
    Gamma
}

/// <summary>
/// Rendering intent for color management.
/// </summary>
public enum RenderingIntent
{
    /// <summary>Perceptual rendering intent.</summary>
    Perceptual = 0,
    /// <summary>Relative colorimetric rendering intent.</summary>
    Relative = 1,
    /// <summary>Saturation rendering intent.</summary>
    Saturation = 2,
    /// <summary>Absolute colorimetric rendering intent.</summary>
    Absolute = 3
}

/// <summary>
/// Custom chromaticity coordinates for white point.
/// </summary>
public readonly record struct JxlCustomWhitePoint(float X, float Y);

/// <summary>
/// Custom chromaticity coordinates for color primaries.
/// </summary>
public readonly record struct JxlCustomPrimaries(
    float Rx, float Ry,
    float Gx, float Gy,
    float Bx, float By);

/// <summary>
/// A unified color profile that represents either an ICC profile or a simple parameterized encoding.
/// This class holds both the profile data and a native handle for helper methods.
/// </summary>
public sealed unsafe class JxlColorProfile : IDisposable
{
    private JxlColorProfileHandle* _handle;
    private bool _disposed;

    // ========================================================================
    // Primary discriminator
    // ========================================================================

    /// <summary>
    /// Gets the type of this color profile.
    /// </summary>
    public JxlProfileType Type { get; }

    // ========================================================================
    // ICC variant (Type == Icc)
    // ========================================================================

    /// <summary>
    /// Gets the ICC profile data. Only valid when <see cref="Type"/> is <see cref="JxlProfileType.Icc"/>.
    /// </summary>
    public byte[]? IccData { get; }

    // ========================================================================
    // Simple encoding components (Type == Rgb, Grayscale, or Xyb)
    // ========================================================================

    /// <summary>
    /// Gets the white point type. Valid for Rgb and Grayscale profiles.
    /// </summary>
    public JxlWhitePointType? WhitePointType { get; }

    /// <summary>
    /// Gets the custom white point coordinates. Only valid when <see cref="WhitePointType"/> is <see cref="JxlWhitePointType.Custom"/>.
    /// </summary>
    public JxlCustomWhitePoint? CustomWhitePoint { get; }

    /// <summary>
    /// Gets the color primaries type. Only valid for Rgb profiles.
    /// </summary>
    public JxlPrimariesType? PrimariesType { get; }

    /// <summary>
    /// Gets the custom primaries coordinates. Only valid when <see cref="PrimariesType"/> is <see cref="JxlPrimariesType.Custom"/>.
    /// </summary>
    public JxlCustomPrimaries? CustomPrimaries { get; }

    /// <summary>
    /// Gets the transfer function type. Valid for Rgb and Grayscale profiles (not Xyb).
    /// </summary>
    public JxlTransferFunctionType? TransferFunctionType { get; }

    /// <summary>
    /// Gets the custom gamma value. Only valid when <see cref="TransferFunctionType"/> is <see cref="JxlTransferFunctionType.Gamma"/>.
    /// </summary>
    public float? GammaValue { get; }

    /// <summary>
    /// Gets the rendering intent.
    /// </summary>
    public RenderingIntent Intent { get; }

    // ========================================================================
    // Convenience booleans
    // ========================================================================

    /// <summary>Gets whether this is an ICC profile.</summary>
    public bool IsIcc => Type == JxlProfileType.Icc;

    /// <summary>Gets whether this is a simple (non-ICC) profile.</summary>
    public bool IsSimple => Type != JxlProfileType.Icc;

    /// <summary>Gets whether this is an RGB color space.</summary>
    public bool IsRgb => Type == JxlProfileType.Rgb;

    /// <summary>Gets whether this is a grayscale color space.</summary>
    public bool IsGrayscale => Type == JxlProfileType.Grayscale;

    /// <summary>Gets whether this is the XYB internal color space.</summary>
    public bool IsXyb => Type == JxlProfileType.Xyb;

    /// <summary>Gets whether this profile uses HDR PQ transfer function.</summary>
    public bool IsPq => TransferFunctionType == JxlTransferFunctionType.Pq;

    /// <summary>Gets whether this profile uses HDR HLG transfer function.</summary>
    public bool IsHlg => TransferFunctionType == JxlTransferFunctionType.Hlg;

    /// <summary>Gets whether this is an HDR profile (PQ or HLG).</summary>
    public bool IsHdr => IsPq || IsHlg;

    /// <summary>Gets whether this profile uses linear transfer function.</summary>
    public bool IsLinear => TransferFunctionType == JxlTransferFunctionType.Linear;

    /// <summary>Gets whether this is a standard sRGB encoding.</summary>
    public bool IsSrgbEncoding => Type == JxlProfileType.Rgb
        && WhitePointType == JxlWhitePointType.D65
        && PrimariesType == JxlPrimariesType.Srgb
        && TransferFunctionType == JxlTransferFunctionType.Srgb;

    // ========================================================================
    // Native handle helpers
    // ========================================================================

    /// <summary>
    /// Gets the number of color channels (1 for grayscale, 3 for RGB, 4 for CMYK).
    /// </summary>
    public int Channels
    {
        get
        {
            ThrowIfDisposed();
            return (int)NativeMethods.jxl_color_profile_channels(_handle);
        }
    }

    /// <summary>
    /// Gets whether this profile represents a CMYK color space.
    /// </summary>
    public bool IsCmyk
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.jxl_color_profile_is_cmyk(_handle);
        }
    }

    /// <summary>
    /// Gets whether the decoder can output to this profile without a CMS.
    /// </summary>
    public bool CanOutputTo
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.jxl_color_profile_can_output_to(_handle);
        }
    }

    // ========================================================================
    // Constructors
    // ========================================================================

    internal JxlColorProfile(
        JxlColorProfileRaw raw,
        byte* iccData,
        JxlColorProfileHandle* handle)
    {
        _handle = handle;

        if (raw.Tag == JxlColorProfileTag.Icc)
        {
            Type = JxlProfileType.Icc;
            var length = (int)raw.IccLength;
            if (length > 0 && iccData != null)
            {
                IccData = new byte[length];
                Marshal.Copy((IntPtr)iccData, IccData, 0, length);
            }
            else
            {
                IccData = Array.Empty<byte>();
            }
            Intent = RenderingIntent.Perceptual;
        }
        else
        {
            // Simple encoding
            var encoding = raw.Encoding;
            Intent = (RenderingIntent)encoding.RenderingIntent;

            Type = encoding.Tag switch
            {
                JxlColorEncodingTag.Rgb => JxlProfileType.Rgb,
                JxlColorEncodingTag.Grayscale => JxlProfileType.Grayscale,
                JxlColorEncodingTag.Xyb => JxlProfileType.Xyb,
                _ => throw new ArgumentException($"Unknown encoding tag: {encoding.Tag}")
            };

            // Parse white point (for Rgb and Grayscale)
            if (Type != JxlProfileType.Xyb)
            {
                WhitePointType = encoding.WhitePoint.Tag switch
                {
                    JxlWhitePointTag.D65 => JxlWhitePointType.D65,
                    JxlWhitePointTag.E => JxlWhitePointType.E,
                    JxlWhitePointTag.Dci => JxlWhitePointType.Dci,
                    JxlWhitePointTag.Chromaticity => JxlWhitePointType.Custom,
                    _ => throw new ArgumentException($"Unknown white point tag: {encoding.WhitePoint.Tag}")
                };

                if (WhitePointType == JxlWhitePointType.Custom)
                {
                    CustomWhitePoint = new JxlCustomWhitePoint(encoding.WhitePoint.Wx, encoding.WhitePoint.Wy);
                }

                // Parse transfer function
                TransferFunctionType = encoding.TransferFunction.Tag switch
                {
                    JxlTransferFunctionTag.Bt709 => JxlTransferFunctionType.Bt709,
                    JxlTransferFunctionTag.Linear => JxlTransferFunctionType.Linear,
                    JxlTransferFunctionTag.Srgb => JxlTransferFunctionType.Srgb,
                    JxlTransferFunctionTag.Pq => JxlTransferFunctionType.Pq,
                    JxlTransferFunctionTag.Dci => JxlTransferFunctionType.Dci,
                    JxlTransferFunctionTag.Hlg => JxlTransferFunctionType.Hlg,
                    JxlTransferFunctionTag.Gamma => JxlTransferFunctionType.Gamma,
                    _ => throw new ArgumentException($"Unknown transfer function tag: {encoding.TransferFunction.Tag}")
                };

                if (TransferFunctionType == JxlTransferFunctionType.Gamma)
                {
                    GammaValue = encoding.TransferFunction.Gamma;
                }
            }

            // Parse primaries (for Rgb only)
            if (Type == JxlProfileType.Rgb)
            {
                PrimariesType = encoding.Primaries.Tag switch
                {
                    JxlPrimariesTag.Srgb => JxlPrimariesType.Srgb,
                    JxlPrimariesTag.Bt2100 => JxlPrimariesType.Bt2100,
                    JxlPrimariesTag.P3 => JxlPrimariesType.P3,
                    JxlPrimariesTag.Chromaticities => JxlPrimariesType.Custom,
                    _ => throw new ArgumentException($"Unknown primaries tag: {encoding.Primaries.Tag}")
                };

                if (PrimariesType == JxlPrimariesType.Custom)
                {
                    CustomPrimaries = new JxlCustomPrimaries(
                        encoding.Primaries.Rx, encoding.Primaries.Ry,
                        encoding.Primaries.Gx, encoding.Primaries.Gy,
                        encoding.Primaries.Bx, encoding.Primaries.By);
                }
            }
        }
    }

    // ========================================================================
    // Factory methods
    // ========================================================================

    /// <summary>
    /// Creates a standard sRGB color profile.
    /// </summary>
    /// <param name="grayscale">If true, creates grayscale sRGB; otherwise RGB sRGB.</param>
    public static JxlColorProfile CreateSrgb(bool grayscale = false)
    {
        var raw = new JxlColorEncodingRaw();
        NativeMethods.jxl_color_encoding_srgb(grayscale, &raw);

        var handle = NativeMethods.jxl_color_profile_from_encoding(&raw);
        if (handle == null)
        {
            throw new JxlException(JxlStatus.Error, "Failed to create sRGB color profile");
        }

        return new JxlColorProfile(
            new JxlColorProfileRaw
            {
                Tag = JxlColorProfileTag.Simple,
                IccLength = UIntPtr.Zero,
                Encoding = raw
            },
            null,
            handle);
    }

    /// <summary>
    /// Creates a linear sRGB color profile.
    /// </summary>
    /// <param name="grayscale">If true, creates grayscale linear sRGB; otherwise RGB linear sRGB.</param>
    public static JxlColorProfile CreateLinearSrgb(bool grayscale = false)
    {
        var raw = new JxlColorEncodingRaw();
        NativeMethods.jxl_color_encoding_linear_srgb(grayscale, &raw);

        var handle = NativeMethods.jxl_color_profile_from_encoding(&raw);
        if (handle == null)
        {
            throw new JxlException(JxlStatus.Error, "Failed to create linear sRGB color profile");
        }

        return new JxlColorProfile(
            new JxlColorProfileRaw
            {
                Tag = JxlColorProfileTag.Simple,
                IccLength = UIntPtr.Zero,
                Encoding = raw
            },
            null,
            handle);
    }

    /// <summary>
    /// Creates a color profile from a simple color encoding.
    /// </summary>
    public static JxlColorProfile FromEncoding(
        JxlProfileType type,
        JxlWhitePointType? whitePoint = null,
        JxlCustomWhitePoint? customWhitePoint = null,
        JxlPrimariesType? primaries = null,
        JxlCustomPrimaries? customPrimaries = null,
        JxlTransferFunctionType? transferFunction = null,
        float? gamma = null,
        RenderingIntent intent = RenderingIntent.Perceptual)
    {
        if (type == JxlProfileType.Icc)
        {
            throw new ArgumentException("Use FromIcc() for ICC profiles", nameof(type));
        }

        var raw = new JxlColorEncodingRaw
        {
            Tag = type switch
            {
                JxlProfileType.Rgb => JxlColorEncodingTag.Rgb,
                JxlProfileType.Grayscale => JxlColorEncodingTag.Grayscale,
                JxlProfileType.Xyb => JxlColorEncodingTag.Xyb,
                _ => throw new ArgumentException($"Invalid profile type: {type}")
            },
            RenderingIntent = (JxlRenderingIntent)intent
        };

        if (type != JxlProfileType.Xyb)
        {
            raw.WhitePoint = (whitePoint ?? JxlWhitePointType.D65) switch
            {
                JxlWhitePointType.D65 => new JxlWhitePointRaw { Tag = JxlWhitePointTag.D65 },
                JxlWhitePointType.E => new JxlWhitePointRaw { Tag = JxlWhitePointTag.E },
                JxlWhitePointType.Dci => new JxlWhitePointRaw { Tag = JxlWhitePointTag.Dci },
                JxlWhitePointType.Custom when customWhitePoint.HasValue => new JxlWhitePointRaw
                {
                    Tag = JxlWhitePointTag.Chromaticity,
                    Wx = customWhitePoint.Value.X,
                    Wy = customWhitePoint.Value.Y
                },
                JxlWhitePointType.Custom => throw new ArgumentException("Custom white point requires customWhitePoint parameter"),
                _ => throw new ArgumentException($"Invalid white point type: {whitePoint}")
            };

            raw.TransferFunction = (transferFunction ?? JxlTransferFunctionType.Srgb) switch
            {
                JxlTransferFunctionType.Bt709 => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Bt709 },
                JxlTransferFunctionType.Linear => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Linear },
                JxlTransferFunctionType.Srgb => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Srgb },
                JxlTransferFunctionType.Pq => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Pq },
                JxlTransferFunctionType.Dci => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Dci },
                JxlTransferFunctionType.Hlg => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Hlg },
                JxlTransferFunctionType.Gamma when gamma.HasValue => new JxlTransferFunctionRaw
                {
                    Tag = JxlTransferFunctionTag.Gamma,
                    Gamma = gamma.Value
                },
                JxlTransferFunctionType.Gamma => throw new ArgumentException("Gamma transfer function requires gamma parameter"),
                _ => throw new ArgumentException($"Invalid transfer function type: {transferFunction}")
            };
        }

        if (type == JxlProfileType.Rgb)
        {
            raw.Primaries = (primaries ?? JxlPrimariesType.Srgb) switch
            {
                JxlPrimariesType.Srgb => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Srgb },
                JxlPrimariesType.Bt2100 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Bt2100 },
                JxlPrimariesType.P3 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.P3 },
                JxlPrimariesType.Custom when customPrimaries.HasValue => new JxlPrimariesRaw
                {
                    Tag = JxlPrimariesTag.Chromaticities,
                    Rx = customPrimaries.Value.Rx, Ry = customPrimaries.Value.Ry,
                    Gx = customPrimaries.Value.Gx, Gy = customPrimaries.Value.Gy,
                    Bx = customPrimaries.Value.Bx, By = customPrimaries.Value.By
                },
                JxlPrimariesType.Custom => throw new ArgumentException("Custom primaries requires customPrimaries parameter"),
                _ => throw new ArgumentException($"Invalid primaries type: {primaries}")
            };
        }

        var handle = NativeMethods.jxl_color_profile_from_encoding(&raw);
        if (handle == null)
        {
            throw new JxlException(JxlStatus.Error, "Failed to create color profile from encoding");
        }

        return new JxlColorProfile(
            new JxlColorProfileRaw
            {
                Tag = JxlColorProfileTag.Simple,
                IccLength = UIntPtr.Zero,
                Encoding = raw
            },
            null,
            handle);
    }

    /// <summary>
    /// Creates a color profile from ICC profile data.
    /// </summary>
    public static JxlColorProfile FromIcc(byte[] iccData)
    {
        if (iccData == null || iccData.Length == 0)
        {
            throw new ArgumentException("ICC data cannot be null or empty", nameof(iccData));
        }

        fixed (byte* dataPtr = iccData)
        {
            var handle = NativeMethods.jxl_color_profile_from_icc(dataPtr, (UIntPtr)iccData.Length);
            if (handle == null)
            {
                throw new JxlException(JxlStatus.Error, "Failed to create color profile from ICC data");
            }

            var raw = new JxlColorProfileRaw
            {
                Tag = JxlColorProfileTag.Icc,
                IccLength = (UIntPtr)iccData.Length,
                Encoding = default
            };

            return new JxlColorProfile(raw, dataPtr, handle);
        }
    }

    // ========================================================================
    // Helper methods
    // ========================================================================

    /// <summary>
    /// Attempts to get ICC profile data. For ICC profiles, returns the original data.
    /// For simple profiles, jxl-rs can convert to ICC.
    /// </summary>
    /// <returns>ICC data if available, null otherwise.</returns>
    public byte[]? TryAsIcc()
    {
        ThrowIfDisposed();

        byte* dataPtr;
        UIntPtr length;

        if (NativeMethods.jxl_color_profile_try_as_icc(_handle, &dataPtr, &length))
        {
            var len = (int)length;
            var result = new byte[len];
            Marshal.Copy((IntPtr)dataPtr, result, 0, len);
            return result;
        }

        return null;
    }

    /// <summary>
    /// Checks if this profile and another represent the same color encoding.
    /// </summary>
    public bool SameColorEncoding(JxlColorProfile other)
    {
        ThrowIfDisposed();
        if (other == null || other._disposed)
        {
            return false;
        }
        return NativeMethods.jxl_color_profile_same_color_encoding(_handle, other._handle);
    }

    /// <summary>
    /// Creates a copy of this profile with linear transfer function.
    /// Returns null if not possible (e.g., for ICC profiles).
    /// </summary>
    public JxlColorProfile? WithLinearTransferFunction()
    {
        ThrowIfDisposed();

        var newHandle = NativeMethods.jxl_color_profile_with_linear_tf(_handle);
        if (newHandle == null)
        {
            return null;
        }

        // Reconstruct the raw data for the new profile
        var newRaw = new JxlColorEncodingRaw
        {
            Tag = Type switch
            {
                JxlProfileType.Rgb => JxlColorEncodingTag.Rgb,
                JxlProfileType.Grayscale => JxlColorEncodingTag.Grayscale,
                _ => JxlColorEncodingTag.Rgb // XYB converts to RGB
            },
            RenderingIntent = (JxlRenderingIntent)Intent,
            WhitePoint = WhitePointType switch
            {
                JxlWhitePointType.D65 => new JxlWhitePointRaw { Tag = JxlWhitePointTag.D65 },
                JxlWhitePointType.E => new JxlWhitePointRaw { Tag = JxlWhitePointTag.E },
                JxlWhitePointType.Dci => new JxlWhitePointRaw { Tag = JxlWhitePointTag.Dci },
                JxlWhitePointType.Custom when CustomWhitePoint.HasValue => new JxlWhitePointRaw
                {
                    Tag = JxlWhitePointTag.Chromaticity,
                    Wx = CustomWhitePoint.Value.X,
                    Wy = CustomWhitePoint.Value.Y
                },
                _ => new JxlWhitePointRaw { Tag = JxlWhitePointTag.D65 }
            },
            TransferFunction = new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Linear },
            Primaries = PrimariesType switch
            {
                JxlPrimariesType.Srgb => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Srgb },
                JxlPrimariesType.Bt2100 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Bt2100 },
                JxlPrimariesType.P3 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.P3 },
                JxlPrimariesType.Custom when CustomPrimaries.HasValue => new JxlPrimariesRaw
                {
                    Tag = JxlPrimariesTag.Chromaticities,
                    Rx = CustomPrimaries.Value.Rx, Ry = CustomPrimaries.Value.Ry,
                    Gx = CustomPrimaries.Value.Gx, Gy = CustomPrimaries.Value.Gy,
                    Bx = CustomPrimaries.Value.Bx, By = CustomPrimaries.Value.By
                },
                _ => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Srgb }
            }
        };

        return new JxlColorProfile(
            new JxlColorProfileRaw
            {
                Tag = JxlColorProfileTag.Simple,
                IccLength = UIntPtr.Zero,
                Encoding = newRaw
            },
            null,
            newHandle);
    }

    /// <summary>
    /// Gets the description of this color profile (e.g., "sRGB", "DisplayP3", "Rec2100PQ").
    /// </summary>
    public string GetDescription()
    {
        ThrowIfDisposed();

        if (IsIcc)
        {
            return "ICC";
        }

        var raw = ToEncodingRaw();
        var size = NativeMethods.jxl_color_encoding_get_description(&raw, null, UIntPtr.Zero);
        if (size == UIntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = stackalloc byte[(int)size + 1];
        NativeMethods.jxl_color_encoding_get_description(&raw, buffer, size);
        return Encoding.UTF8.GetString(buffer, (int)size);
    }

    /// <summary>
    /// Gets the string representation of this color profile.
    /// </summary>
    public override string ToString()
    {
        if (_disposed)
        {
            return "<disposed>";
        }

        var size = NativeMethods.jxl_color_profile_to_string(_handle, null, UIntPtr.Zero);
        if (size == UIntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = stackalloc byte[(int)size + 1];
        NativeMethods.jxl_color_profile_to_string(_handle, buffer, size);
        return Encoding.UTF8.GetString(buffer, (int)size);
    }

    /// <summary>
    /// Gets the native handle for interop purposes.
    /// </summary>
    internal JxlColorProfileHandle* Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    internal JxlColorEncodingRaw ToEncodingRaw()
    {
        return new JxlColorEncodingRaw
        {
            Tag = Type switch
            {
                JxlProfileType.Rgb => JxlColorEncodingTag.Rgb,
                JxlProfileType.Grayscale => JxlColorEncodingTag.Grayscale,
                JxlProfileType.Xyb => JxlColorEncodingTag.Xyb,
                _ => throw new InvalidOperationException("Cannot convert ICC profile to encoding")
            },
            RenderingIntent = (JxlRenderingIntent)Intent,
            WhitePoint = WhitePointType switch
            {
                JxlWhitePointType.D65 => new JxlWhitePointRaw { Tag = JxlWhitePointTag.D65 },
                JxlWhitePointType.E => new JxlWhitePointRaw { Tag = JxlWhitePointTag.E },
                JxlWhitePointType.Dci => new JxlWhitePointRaw { Tag = JxlWhitePointTag.Dci },
                JxlWhitePointType.Custom when CustomWhitePoint.HasValue => new JxlWhitePointRaw
                {
                    Tag = JxlWhitePointTag.Chromaticity,
                    Wx = CustomWhitePoint.Value.X,
                    Wy = CustomWhitePoint.Value.Y
                },
                _ => default
            },
            Primaries = PrimariesType switch
            {
                JxlPrimariesType.Srgb => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Srgb },
                JxlPrimariesType.Bt2100 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.Bt2100 },
                JxlPrimariesType.P3 => new JxlPrimariesRaw { Tag = JxlPrimariesTag.P3 },
                JxlPrimariesType.Custom when CustomPrimaries.HasValue => new JxlPrimariesRaw
                {
                    Tag = JxlPrimariesTag.Chromaticities,
                    Rx = CustomPrimaries.Value.Rx, Ry = CustomPrimaries.Value.Ry,
                    Gx = CustomPrimaries.Value.Gx, Gy = CustomPrimaries.Value.Gy,
                    Bx = CustomPrimaries.Value.Bx, By = CustomPrimaries.Value.By
                },
                _ => default
            },
            TransferFunction = TransferFunctionType switch
            {
                JxlTransferFunctionType.Bt709 => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Bt709 },
                JxlTransferFunctionType.Linear => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Linear },
                JxlTransferFunctionType.Srgb => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Srgb },
                JxlTransferFunctionType.Pq => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Pq },
                JxlTransferFunctionType.Dci => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Dci },
                JxlTransferFunctionType.Hlg => new JxlTransferFunctionRaw { Tag = JxlTransferFunctionTag.Hlg },
                JxlTransferFunctionType.Gamma when GammaValue.HasValue => new JxlTransferFunctionRaw
                {
                    Tag = JxlTransferFunctionTag.Gamma,
                    Gamma = GammaValue.Value
                },
                _ => default
            }
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(JxlColorProfile));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != null)
            {
                NativeMethods.jxl_color_profile_free(_handle);
                _handle = null;
            }
            _disposed = true;
        }
    }
}
