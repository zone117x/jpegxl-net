using JpegXL.Net;

namespace JpegXL.Net.Tests;

[TestClass]
public class ColorProfileTests
{
    [TestMethod]
    public void CreateSrgb_ReturnsValidProfile()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.AreEqual(JxlProfileType.Rgb, profile.Type);
        Assert.AreEqual(JxlWhitePointType.D65, profile.WhitePointType);
        Assert.AreEqual(JxlPrimariesType.Srgb, profile.PrimariesType);
        Assert.AreEqual(JxlTransferFunctionType.Srgb, profile.TransferFunctionType);
    }

    [TestMethod]
    public void CreateSrgb_Grayscale_ReturnsGrayscaleProfile()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb(grayscale: true);

        // Assert
        Assert.AreEqual(JxlProfileType.Grayscale, profile.Type);
        Assert.AreEqual(JxlWhitePointType.D65, profile.WhitePointType);
        Assert.AreEqual(JxlTransferFunctionType.Srgb, profile.TransferFunctionType);
        Assert.IsNull(profile.PrimariesType); // Grayscale has no primaries
    }

    [TestMethod]
    public void CreateLinearSrgb_ReturnsLinearTransferFunction()
    {
        // Act
        using var profile = JxlColorProfile.CreateLinearSrgb();

        // Assert
        Assert.AreEqual(JxlProfileType.Rgb, profile.Type);
        Assert.AreEqual(JxlTransferFunctionType.Linear, profile.TransferFunctionType);
        Assert.IsTrue(profile.IsLinear);
    }

    [TestMethod]
    public void CreateLinearSrgb_Grayscale_ReturnsLinearGrayscale()
    {
        // Act
        using var profile = JxlColorProfile.CreateLinearSrgb(grayscale: true);

        // Assert
        Assert.AreEqual(JxlProfileType.Grayscale, profile.Type);
        Assert.AreEqual(JxlTransferFunctionType.Linear, profile.TransferFunctionType);
        Assert.IsTrue(profile.IsGrayscale);
        Assert.IsTrue(profile.IsLinear);
    }

    [TestMethod]
    public void Channels_SrgbProfile_Returns3()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.AreEqual(3, profile.Channels);
    }

    [TestMethod]
    public void Channels_GrayscaleProfile_Returns1()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb(grayscale: true);

        // Assert
        Assert.AreEqual(1, profile.Channels);
    }

    [TestMethod]
    public void IsCmyk_SrgbProfile_ReturnsFalse()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.IsFalse(profile.IsCmyk);
    }

    [TestMethod]
    public void CanOutputTo_SrgbProfile_ReturnsTrue()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.IsTrue(profile.CanOutputTo);
    }

    [TestMethod]
    public void TransferFunction_SrgbProfile_ReturnsSrgb()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.IsNotNull(profile.TransferFunctionType);
        Assert.AreEqual(JxlTransferFunctionType.Srgb, profile.TransferFunctionType);
    }

    [TestMethod]
    public void TransferFunction_LinearProfile_ReturnsLinear()
    {
        // Act
        using var profile = JxlColorProfile.CreateLinearSrgb();

        // Assert
        Assert.IsNotNull(profile.TransferFunctionType);
        Assert.AreEqual(JxlTransferFunctionType.Linear, profile.TransferFunctionType);
    }

    [TestMethod]
    public void SameColorEncoding_IdenticalProfiles_ReturnsTrue()
    {
        // Arrange
        using var profile1 = JxlColorProfile.CreateSrgb();
        using var profile2 = JxlColorProfile.CreateSrgb();

        // Assert
        Assert.IsTrue(profile1.SameColorEncoding(profile2));
    }

    [TestMethod]
    public void SameColorEncoding_DifferentProfiles_ReturnsFalse()
    {
        // Arrange
        using var srgbProfile = JxlColorProfile.CreateSrgb();
        using var linearProfile = JxlColorProfile.CreateLinearSrgb();

        // Assert
        Assert.IsFalse(srgbProfile.SameColorEncoding(linearProfile));
    }

    [TestMethod]
    public void WithLinearTransferFunction_SrgbProfile_ReturnsLinear()
    {
        // Arrange
        using var srgbProfile = JxlColorProfile.CreateSrgb();

        // Act
        using var linearProfile = srgbProfile.WithLinearTransferFunction();

        // Assert
        Assert.IsNotNull(linearProfile);
        Assert.AreEqual(JxlTransferFunctionType.Linear, linearProfile.TransferFunctionType);
        Assert.IsTrue(linearProfile.IsLinear);
    }

    [TestMethod]
    public void ToString_SrgbProfile_ReturnsNonEmptyString()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();
        var str = profile.ToString();

        // Assert
        Assert.IsNotNull(str);
        Assert.IsTrue(str.Length > 0, "ToString should return non-empty string");
        // ToString should contain recognizable profile info
        Assert.IsTrue(str.Contains("RGB") || str.Contains("sRGB") || str.Contains("D65"),
            $"ToString should contain profile identifiers, got: {str}");
    }

    [TestMethod]
    public void GetDescription_SrgbProfile_ReturnsDescription()
    {
        // Act
        using var profile = JxlColorProfile.CreateSrgb();
        var description = profile.GetDescription();

        // Assert
        Assert.IsNotNull(description);
        Assert.AreEqual("RGB_D65_SRG_Rel_SRG", description,
            "sRGB profile should have standard encoded description");
    }

    [TestMethod]
    public void FromEncoding_CustomEncoding_CreatesProfile()
    {
        // Act - Create P3 RGB profile
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.P3,
            transferFunction: JxlTransferFunctionType.Srgb,
            intent: RenderingIntent.Perceptual);

        // Assert
        Assert.AreEqual(JxlProfileType.Rgb, profile.Type);
        Assert.AreEqual(JxlPrimariesType.P3, profile.PrimariesType);
        Assert.AreEqual(JxlWhitePointType.D65, profile.WhitePointType);
        Assert.AreEqual(JxlTransferFunctionType.Srgb, profile.TransferFunctionType);
    }

    [TestMethod]
    public void GetEmbeddedColorProfile_SrgbImage_ReturnsProfile()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert
        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.IsIcc);
        Assert.AreEqual(3, profile.Channels, "sRGB image should have 3 channels");
        Assert.IsTrue(profile.IsRgb, "Should be RGB profile");
    }

    [TestMethod]
    public void GetOutputColorProfile_AfterReadInfo_ReturnsProfile()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetOutputColorProfile();

        // Assert
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.Channels >= 1 && profile.Channels <= 4,
            $"Output profile should have 1-4 channels, got {profile.Channels}");
    }

    [TestMethod]
    public void SetOutputColorProfile_SrgbProfile_Succeeds()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        using var srgbProfile = JxlColorProfile.CreateSrgb();

        // Act & Assert (should not throw)
        decoder.SetOutputColorProfile(srgbProfile);

        // Verify the profile was set
        using var outputProfile = decoder.GetOutputColorProfile();
        Assert.IsTrue(outputProfile.SameColorEncoding(srgbProfile));
    }

    [TestMethod]
    public void SetOutputColorProfileSrgb_Convenience_Succeeds()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert (should not throw)
        decoder.SetOutputColorProfileSrgb();

        // Verify output profile is sRGB
        using var outputProfile = decoder.GetOutputColorProfile();
        Assert.AreEqual(3, outputProfile.Channels);
        Assert.AreEqual(JxlTransferFunctionType.Srgb, outputProfile.TransferFunctionType);
    }

    [TestMethod]
    public void SetOutputColorProfileLinearSrgb_Convenience_Succeeds()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert (should not throw)
        decoder.SetOutputColorProfileLinearSrgb();

        // Verify output profile is linear sRGB
        using var outputProfile = decoder.GetOutputColorProfile();
        Assert.AreEqual(3, outputProfile.Channels);
        Assert.AreEqual(JxlTransferFunctionType.Linear, outputProfile.TransferFunctionType);
    }

    [TestMethod]
    public void TryAsIcc_SimpleProfile_CanConvertToIcc()
    {
        // Simple color encodings can be converted to ICC data by jxl-rs
        using var profile = JxlColorProfile.CreateSrgb();

        // Act
        var iccData = profile.TryAsIcc();

        // Assert - jxl-rs can convert simple profiles to ICC
        Assert.IsNotNull(iccData);
        Assert.IsTrue(iccData.Length > 128, "ICC data should be larger than header (128 bytes)");

        // Validate ICC magic bytes: "acsp" at offset 36
        Assert.AreEqual((byte)'a', iccData[36], "ICC magic byte 1 should be 'a'");
        Assert.AreEqual((byte)'c', iccData[37], "ICC magic byte 2 should be 'c'");
        Assert.AreEqual((byte)'s', iccData[38], "ICC magic byte 3 should be 's'");
        Assert.AreEqual((byte)'p', iccData[39], "ICC magic byte 4 should be 'p'");
    }

    [TestMethod]
    public void ConvenienceBooleans_WorkCorrectly()
    {
        // Arrange & Act
        using var srgbProfile = JxlColorProfile.CreateSrgb();
        using var linearProfile = JxlColorProfile.CreateLinearSrgb();
        using var grayProfile = JxlColorProfile.CreateSrgb(grayscale: true);

        // Assert - sRGB profile
        Assert.IsTrue(srgbProfile.IsRgb);
        Assert.IsFalse(srgbProfile.IsGrayscale);
        Assert.IsFalse(srgbProfile.IsIcc);
        Assert.IsTrue(srgbProfile.IsSimple);
        Assert.IsFalse(srgbProfile.IsLinear);
        Assert.IsFalse(srgbProfile.IsHdr);
        Assert.IsTrue(srgbProfile.IsSrgbEncoding);

        // Assert - linear profile
        Assert.IsTrue(linearProfile.IsLinear);
        Assert.IsFalse(linearProfile.IsSrgbEncoding);

        // Assert - grayscale profile
        Assert.IsTrue(grayProfile.IsGrayscale);
        Assert.IsFalse(grayProfile.IsRgb);
    }

    [TestMethod]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var profile = JxlColorProfile.CreateSrgb();

        // Act & Assert - multiple dispose calls should not throw
        profile.Dispose();
        profile.Dispose();
        profile.Dispose();
    }

    [TestMethod]
    public void AccessAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var profile = JxlColorProfile.CreateSrgb();
        profile.Dispose();

        // Act & Assert
        Assert.ThrowsException<ObjectDisposedException>(() => _ = profile.Channels);
    }

    [TestMethod]
    public void HdrImage_HasPqTransferFunction()
    {
        // Arrange - HDR PQ test file
        var data = File.ReadAllBytes("TestData/hdr_pq_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - HDR PQ file should be RGB with PQ transfer function
        Assert.IsTrue(profile.IsRgb, "HDR PQ file should have RGB profile");
        Assert.AreEqual(JxlTransferFunctionType.Pq, profile.TransferFunctionType);
        Assert.IsTrue(profile.IsPq);
        Assert.IsTrue(profile.IsHdr);
    }

    [TestMethod]
    public void HdrHlgImage_HasHlgTransferFunction()
    {
        // Arrange - HDR HLG test file
        var data = File.ReadAllBytes("TestData/hdr_hlg_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - HDR HLG file should be RGB with HLG transfer function
        Assert.IsTrue(profile.IsRgb, "HDR HLG file should have RGB profile");
        Assert.AreEqual(JxlTransferFunctionType.Hlg, profile.TransferFunctionType);
        Assert.IsTrue(profile.IsHlg);
        Assert.IsTrue(profile.IsHdr);
    }

    [TestMethod]
    public void IccProfileParser_InvalidData_ReturnsNull()
    {
        // Arrange - too short
        var shortData = new byte[100];

        // Act & Assert
        Assert.IsNull(IccProfileParser.TryGetDescription(shortData));
        Assert.IsNull(IccProfileParser.TryGetDescription([]));
        Assert.IsNull(IccProfileParser.TryGetDescription(new byte[132])); // Valid size but no desc tag
    }

    [TestMethod]
    public void IccProfileParser_SrgbProfile_ReturnsDescription()
    {
        // Arrange - get ICC data from a simple profile
        using var profile = JxlColorProfile.CreateSrgb();
        var iccData = profile.TryAsIcc();

        // jxl-rs should always be able to convert simple profiles to ICC
        Assert.IsNotNull(iccData, "sRGB profile should be convertible to ICC");
        Assert.IsTrue(iccData.Length > 0, "ICC data should not be empty");

        // Act
        var description = IccProfileParser.TryGetDescription(iccData);

        // Assert - jxl-rs generates ICC with description like "RGB_D65_SRG_Rel_SRG"
        Assert.AreEqual("RGB_D65_SRG_Rel_SRG", description);
    }

    [TestMethod]
    public void IccProfileParser_JxlFileWithIcc_ReturnsDescription()
    {
        // Arrange - load a JXL file that has an embedded ICC profile
        var data = File.ReadAllBytes("TestData/with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - this file should have an ICC profile
        Assert.IsTrue(profile.IsIcc, "with_icc.jxl should have an ICC profile");
        Assert.IsNotNull(profile.IccData, "ICC data should not be null");
        Assert.IsTrue(profile.IccData.Length > 0, "ICC data should not be empty");

        // Parse the ICC description
        var description = IccProfileParser.TryGetDescription(profile.IccData);

        // The embedded ICC profile is a GIMP grayscale profile
        Assert.AreEqual("GIMP built-in D65 Grayscale with sRGB TRC", description);
    }

    [TestMethod]
    public void IccProfileParser_GrayscaleWithIcc_ReturnsDescription()
    {
        // Arrange - load a grayscale JXL file with embedded ICC profile
        var data = File.ReadAllBytes("TestData/small_grayscale_patches_modular_with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - this file should have an ICC profile
        Assert.IsTrue(profile.IsIcc, "File should have an ICC profile");
        Assert.AreEqual(1, profile.Channels, "Should be grayscale (1 channel)");

        // Parse the ICC description
        var description = IccProfileParser.TryGetDescription(profile.IccData);

        // The embedded ICC profile is a GIMP grayscale profile
        Assert.AreEqual("GIMP built-in D65 Grayscale with sRGB TRC", description);
    }

    // =========================================================================
    // Color Encoding Comparison Tests (from jxl-rs)
    // =========================================================================

    [TestMethod]
    public void SameColorEncoding_DifferentPrimaries_ReturnsFalse()
    {
        // sRGB vs P3 should be different
        using var srgb = JxlColorProfile.CreateSrgb();
        using var p3 = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.P3,
            transferFunction: JxlTransferFunctionType.Srgb);

        Assert.IsFalse(srgb.SameColorEncoding(p3));
    }

    [TestMethod]
    public void SameColorEncoding_DifferentWhitePoint_ReturnsFalse()
    {
        // D65 vs DCI white point should be different
        using var d65 = JxlColorProfile.CreateSrgb();
        using var dci = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.Dci,
            primaries: JxlPrimariesType.Srgb,
            transferFunction: JxlTransferFunctionType.Srgb);

        Assert.IsFalse(d65.SameColorEncoding(dci));
    }

    [TestMethod]
    public void SameColorEncoding_RgbVsGrayscale_ReturnsFalse()
    {
        using var rgb = JxlColorProfile.CreateSrgb();
        using var gray = JxlColorProfile.CreateSrgb(grayscale: true);

        Assert.IsFalse(rgb.SameColorEncoding(gray));
    }

    // =========================================================================
    // GetDescription Tests
    // =========================================================================

    [TestMethod]
    public void GetDescription_Srgb_ReturnsEncodedString()
    {
        using var profile = JxlColorProfile.CreateSrgb();
        // Format: {ColorSpace}_{WhitePoint}_{Primaries}_{Intent}_{TransferFunction}
        Assert.AreEqual("RGB_D65_SRG_Rel_SRG", profile.GetDescription());
    }

    [TestMethod]
    public void GetDescription_LinearSrgb_ReturnsLinTransferFunction()
    {
        using var profile = JxlColorProfile.CreateLinearSrgb();
        // Linear transfer function is indicated by "Lin" suffix
        Assert.AreEqual("RGB_D65_SRG_Rel_Lin", profile.GetDescription());
    }

    [TestMethod]
    public void GetDescription_DisplayP3_ReturnsDisplayP3()
    {
        // Display P3 (D65 white point, P3 primaries, sRGB TF) is a well-known profile
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.P3,
            transferFunction: JxlTransferFunctionType.Srgb);

        Assert.AreEqual("DisplayP3", profile.GetDescription());
    }

    [TestMethod]
    public void GetDescription_Rec2100Pq_ReturnsEncodedString()
    {
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.Bt2100,
            transferFunction: JxlTransferFunctionType.Pq);

        // 202 = BT.2020/2100 primaries, PeQ = PQ transfer function
        Assert.AreEqual("RGB_D65_202_Per_PeQ", profile.GetDescription());
    }

    [TestMethod]
    public void GetDescription_Rec2100Hlg_ReturnsEncodedString()
    {
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.Bt2100,
            transferFunction: JxlTransferFunctionType.Hlg);

        // 202 = BT.2020/2100 primaries, HLG = HLG transfer function
        Assert.AreEqual("RGB_D65_202_Per_HLG", profile.GetDescription());
    }

    // =========================================================================
    // Custom Profile Creation Tests
    // =========================================================================

    [TestMethod]
    public void FromEncoding_Bt2100Pq_CreatesHdrProfile()
    {
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.Bt2100,
            transferFunction: JxlTransferFunctionType.Pq);

        Assert.IsTrue(profile.IsRgb);
        Assert.IsTrue(profile.IsHdr);
        Assert.IsTrue(profile.IsPq);
        Assert.AreEqual(JxlPrimariesType.Bt2100, profile.PrimariesType);
    }

    [TestMethod]
    public void FromEncoding_Bt2100Hlg_CreatesHdrProfile()
    {
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.Bt2100,
            transferFunction: JxlTransferFunctionType.Hlg);

        Assert.IsTrue(profile.IsRgb);
        Assert.IsTrue(profile.IsHdr);
        Assert.IsTrue(profile.IsHlg);
    }

    [TestMethod]
    public void FromEncoding_DisplayP3_CreatesWideGamutProfile()
    {
        using var profile = JxlColorProfile.FromEncoding(
            JxlProfileType.Rgb,
            whitePoint: JxlWhitePointType.D65,
            primaries: JxlPrimariesType.P3,
            transferFunction: JxlTransferFunctionType.Srgb);

        Assert.IsTrue(profile.IsRgb);
        Assert.IsFalse(profile.IsHdr);
        Assert.AreEqual(JxlPrimariesType.P3, profile.PrimariesType);
    }

    // =========================================================================
    // PQ Gradient Test File
    // =========================================================================

    [TestMethod]
    public void PqGradient_HasPqTransferFunction()
    {
        var data = File.ReadAllBytes("TestData/pq_gradient.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        using var profile = decoder.GetEmbeddedColorProfile();

        // pq_gradient.jxl is a grayscale image with PQ transfer function
        Assert.IsTrue(profile.IsGrayscale, "PQ gradient is grayscale");
        Assert.AreEqual(JxlTransferFunctionType.Pq, profile.TransferFunctionType);
        Assert.IsTrue(profile.IsPq);
        Assert.IsTrue(profile.IsHdr);
        Assert.AreEqual("Gra_D65_Rel_PeQ", profile.GetDescription());
    }
}
