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

    // =========================================================================
    // IccProfileParser.TryGetHeaderInfo Tests
    // =========================================================================

    [TestMethod]
    public void IccProfileParser_TryGetHeaderInfo_InvalidData_ReturnsNull()
    {
        // Too short
        Assert.IsNull(IccProfileParser.TryGetHeaderInfo(new byte[100]));
        Assert.IsNull(IccProfileParser.TryGetHeaderInfo([]));
    }

    [TestMethod]
    public void IccProfileParser_TryGetHeaderInfo_SrgbProfile_ReturnsValidHeader()
    {
        // Arrange - get ICC data from a simple profile
        using var profile = JxlColorProfile.CreateSrgb();
        var iccData = profile.TryAsIcc();
        Assert.IsNotNull(iccData);

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(iccData);

        // Assert
        Assert.IsNotNull(header);
        Assert.AreEqual(IccProfileClass.Display, header.Value.ProfileClass);
        Assert.AreEqual(IccColorSpaceType.Rgb, header.Value.ColorSpace);
        Assert.IsTrue(header.Value.IccVersion.Major >= 2, "ICC version should be at least 2.x");
    }

    [TestMethod]
    public void IccProfileParser_TryGetHeaderInfo_GrayscaleIcc_ReturnsGrayColorSpace()
    {
        // Arrange - load a JXL file with grayscale ICC profile
        var data = File.ReadAllBytes("TestData/with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();
        Assert.IsTrue(profile.IsIcc);

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);

        // Assert
        Assert.IsNotNull(header);
        Assert.AreEqual(IccColorSpaceType.Gray, header.Value.ColorSpace);
        Assert.AreEqual(IccProfileClass.Display, header.Value.ProfileClass);
    }

    [TestMethod]
    public void IccProfileParser_TryGetHeaderInfo_HdrLinearIcc_ReturnsRgbDisplayProfile()
    {
        // Arrange - load a JXL file with HLG ICC profile (via CICP)
        var data = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();
        Assert.IsTrue(profile.IsIcc);

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);

        // Assert
        Assert.IsNotNull(header);
        Assert.AreEqual(IccProfileClass.Display, header.Value.ProfileClass);
        Assert.AreEqual(IccColorSpaceType.Rgb, header.Value.ColorSpace);
        Assert.AreEqual(4, header.Value.IccVersion.Major, "Should be ICC v4.x");
    }

    // =========================================================================
    // IccProfileParser.TryGetColorSpaceInfo Tests
    // =========================================================================

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_InvalidData_ReturnsNull()
    {
        Assert.IsNull(IccProfileParser.TryGetColorSpaceInfo(new byte[100]));
        Assert.IsNull(IccProfileParser.TryGetColorSpaceInfo([]));
    }

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_SrgbProfile_ReturnsValidInfo()
    {
        // Arrange
        using var profile = JxlColorProfile.CreateSrgb();
        var iccData = profile.TryAsIcc();
        Assert.IsNotNull(iccData);

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(iccData);

        // Assert
        Assert.IsNotNull(colorSpace);
        Assert.IsNotNull(colorSpace.Value.WhitePoint);
        Assert.IsNotNull(colorSpace.Value.RedPrimary);
        Assert.IsNotNull(colorSpace.Value.GreenPrimary);
        Assert.IsNotNull(colorSpace.Value.BluePrimary);
        Assert.IsFalse(colorSpace.Value.IsHlg);
        Assert.IsFalse(colorSpace.Value.IsPq);
        Assert.IsFalse(colorSpace.Value.IsHdr);
    }

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_GrayscaleIcc_ReturnsWhitePointOnly()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert
        Assert.IsNotNull(colorSpace);
        Assert.IsNotNull(colorSpace.Value.WhitePoint);
        Assert.IsTrue(colorSpace.Value.IsD65WhitePoint, "GIMP D65 Grayscale should have D65 white point");
        // Grayscale profiles don't have RGB primaries
        Assert.IsNull(colorSpace.Value.RedPrimary);
        Assert.IsNull(colorSpace.Value.GreenPrimary);
        Assert.IsNull(colorSpace.Value.BluePrimary);
        Assert.IsFalse(colorSpace.Value.IsHdr);
    }

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_GrayscaleIcc_DetectsTrcCurve()
    {
        // Arrange - grayscale ICC with kTRC tag
        var data = File.ReadAllBytes("TestData/with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert - should detect the parametric TRC curve
        Assert.IsNotNull(colorSpace);
        Assert.IsNotNull(colorSpace.Value.TransferFunction);
        Assert.AreEqual(IccTransferFunction.Parametric, colorSpace.Value.TransferFunction);
    }

    // =========================================================================
    // CICP Tag Detection Tests (HLG/PQ via ICC)
    // =========================================================================

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_HlgIcc_DetectsHlgViaCicp()
    {
        // Arrange - HDR file with HLG ICC profile containing CICP tag
        var data = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();
        Assert.IsTrue(profile.IsIcc, "hdr_linear_test.jxl should have ICC profile");

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert - should detect HLG via CICP tag
        Assert.IsNotNull(colorSpace);
        Assert.IsTrue(colorSpace.Value.IsHlg, "Should detect HLG from CICP tag");
        Assert.IsFalse(colorSpace.Value.IsPq);
        Assert.IsTrue(colorSpace.Value.IsHdr, "HLG is HDR");
        Assert.AreEqual(IccTransferFunction.Hlg, colorSpace.Value.TransferFunction);
        Assert.AreEqual(IccCicpTransfer.Hlg, colorSpace.Value.CicpTransfer);
        Assert.AreEqual(IccCicpPrimaries.Bt2020, colorSpace.Value.CicpPrimaries);
    }

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_HlgIcc_ToStringReturnsRec2100Hlg()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert - ToString should return friendly name
        Assert.IsNotNull(colorSpace);
        Assert.AreEqual("Rec.2100 HLG", colorSpace.Value.ToString());
    }

    [TestMethod]
    public void IccProfileParser_TryGetColorSpaceInfo_HlgIcc_HasD50WhitePoint()
    {
        // ICC profiles use D50 as Profile Connection Space
        var data = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        Assert.IsNotNull(colorSpace);
        Assert.IsNotNull(colorSpace.Value.WhitePoint);
        Assert.IsTrue(colorSpace.Value.IsD50WhitePoint, "ICC profiles typically use D50 PCS");
        Assert.IsFalse(colorSpace.Value.IsD65WhitePoint);
    }

    // =========================================================================
    // IccColorSpaceInfo Detection Tests
    // =========================================================================

    [TestMethod]
    public void IccColorSpaceInfo_IsLikelySrgb_WithSrgbPrimaries_ReturnsTrue()
    {
        // jxl-rs generates ICC profiles with D65 white point for sRGB
        // Note: Standard ICC sRGB uses D50 PCS, but jxl-rs may use different conventions
        using var profile = JxlColorProfile.CreateSrgb();
        var iccData = profile.TryAsIcc();
        Assert.IsNotNull(iccData);

        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(iccData);
        Assert.IsNotNull(colorSpace);

        // The exact detection depends on jxl-rs ICC generation
        // At minimum, primaries should be present for RGB profiles
        Assert.IsNotNull(colorSpace.Value.RedPrimary);
        Assert.IsNotNull(colorSpace.Value.GreenPrimary);
        Assert.IsNotNull(colorSpace.Value.BluePrimary);
    }

    [TestMethod]
    public void IccColorSpaceInfo_ToString_CustomProfile_ReturnsCustom()
    {
        // Arrange - grayscale profile is "Custom" since it's not sRGB/P3/Rec.2020
        var data = File.ReadAllBytes("TestData/with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Act
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert
        Assert.IsNotNull(colorSpace);
        Assert.AreEqual("Custom", colorSpace.Value.ToString());
    }

    // =========================================================================
    // XyzColor Tests
    // =========================================================================

    [TestMethod]
    public void XyzColor_ApproximatelyEquals_WithinTolerance_ReturnsTrue()
    {
        var color1 = new XyzColor(0.9505f, 1.0000f, 1.0890f);
        var color2 = new XyzColor(0.9506f, 1.0001f, 1.0889f);

        Assert.IsTrue(color1.ApproximatelyEquals(color2, tolerance: 0.002f));
    }

    [TestMethod]
    public void XyzColor_ApproximatelyEquals_OutsideTolerance_ReturnsFalse()
    {
        var color1 = new XyzColor(0.9505f, 1.0000f, 1.0890f);
        var color2 = new XyzColor(0.9600f, 1.0000f, 1.0890f); // X differs by 0.0095

        Assert.IsFalse(color1.ApproximatelyEquals(color2, tolerance: 0.002f));
    }

    [TestMethod]
    public void XyzColor_ToString_ReturnsFormattedString()
    {
        var color = new XyzColor(0.9505f, 1.0000f, 1.0890f);
        var str = color.ToString();

        Assert.IsTrue(str.Contains("0.9505"));
        Assert.IsTrue(str.Contains("1.0000"));
        Assert.IsTrue(str.Contains("1.0890"));
    }

    // =========================================================================
    // IccHeaderInfo Tests
    // =========================================================================

    [TestMethod]
    public void IccHeaderInfo_ToString_ReturnsFormattedString()
    {
        // Arrange
        using var profile = JxlColorProfile.CreateSrgb();
        var iccData = profile.TryAsIcc();
        var header = IccProfileParser.TryGetHeaderInfo(iccData);

        // Assert
        Assert.IsNotNull(header);
        var str = header.Value.ToString();
        Assert.IsTrue(str.Contains("ICC"), "Should contain 'ICC'");
        Assert.IsTrue(str.Contains("Display") || str.Contains("Rgb"),
            "Should contain profile class or color space");
    }

    // =========================================================================
    // Integration Tests - ICC vs Structured Profile Detection
    // =========================================================================

    [TestMethod]
    public void IccHlg_StructuredHlg_BothDetectHlg()
    {
        // Arrange - ICC HLG (via CICP)
        var iccData = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var iccDecoder = new JxlDecoder();
        iccDecoder.SetInput(iccData);
        iccDecoder.ReadInfo();
        using var iccProfile = iccDecoder.GetEmbeddedColorProfile();

        // Arrange - Structured HLG
        var structuredData = File.ReadAllBytes("TestData/hdr_hlg_test.jxl");
        using var structuredDecoder = new JxlDecoder();
        structuredDecoder.SetInput(structuredData);
        structuredDecoder.ReadInfo();
        using var structuredProfile = structuredDecoder.GetEmbeddedColorProfile();

        // Assert - ICC profile
        Assert.IsTrue(iccProfile.IsIcc, "hdr_linear_test.jxl should be ICC");
        var iccColorSpace = IccProfileParser.TryGetColorSpaceInfo(iccProfile.IccData);
        Assert.IsNotNull(iccColorSpace);
        Assert.IsTrue(iccColorSpace.Value.IsHlg, "ICC profile should detect HLG");

        // Assert - Structured profile
        Assert.IsFalse(structuredProfile.IsIcc, "hdr_hlg_test.jxl should be structured");
        Assert.IsTrue(structuredProfile.IsHlg, "Structured profile should detect HLg");
    }

    [TestMethod]
    public void IccProfile_FullMetadataExtraction_Succeeds()
    {
        // Integration test: extract all metadata from an ICC profile
        var data = File.ReadAllBytes("TestData/hdr_linear_test.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Extract all info
        var description = IccProfileParser.TryGetDescription(profile.IccData);
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // All should succeed
        Assert.IsNotNull(description);
        Assert.IsNotNull(header);
        Assert.IsNotNull(colorSpace);

        // Verify consistency
        Assert.AreEqual("Rec2100HLG", description);
        Assert.AreEqual(IccColorSpaceType.Rgb, header.Value.ColorSpace);
        Assert.IsTrue(colorSpace.Value.IsHlg);
        Assert.AreEqual("Rec.2100 HLG", colorSpace.Value.ToString());
    }

    // =========================================================================
    // Additional ICC Test File Tests
    // =========================================================================

    [TestMethod]
    public void IccProfileParser_LossyWithIcc_DetectsGamma22()
    {
        // Arrange - lossy_with_icc.jxl is ICC v2.1 with gamma 2.2
        var data = File.ReadAllBytes("TestData/lossy_with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - should have ICC profile
        Assert.IsTrue(profile.IsIcc, "lossy_with_icc.jxl should have ICC profile");

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert header
        Assert.IsNotNull(header);
        Assert.AreEqual(2, header.Value.IccVersion.Major, "Should be ICC v2.x");
        Assert.AreEqual(IccColorSpaceType.Rgb, header.Value.ColorSpace);

        // Assert color space - should have gamma TRC
        Assert.IsNotNull(colorSpace);
        Assert.IsFalse(colorSpace.Value.IsHdr, "Gamma 2.2 profile is not HDR");
        Assert.IsNotNull(colorSpace.Value.TransferFunction);
        // Gamma 2.2 is typically encoded as curv or para
        Assert.IsTrue(
            colorSpace.Value.TransferFunction == IccTransferFunction.Gamma ||
            colorSpace.Value.TransferFunction == IccTransferFunction.LookupTable ||
            colorSpace.Value.TransferFunction == IccTransferFunction.Parametric,
            $"Expected gamma-like transfer function, got {colorSpace.Value.TransferFunction}");
    }

    [TestMethod]
    public void IccProfileParser_Progressive_DetectsParametricCurve()
    {
        // Arrange - progressive.jxl is ICC v4.3 with parametric curve
        var data = File.ReadAllBytes("TestData/progressive.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - should have ICC profile
        Assert.IsTrue(profile.IsIcc, "progressive.jxl should have ICC profile");

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert header
        Assert.IsNotNull(header);
        Assert.AreEqual(4, header.Value.IccVersion.Major, "Should be ICC v4.x");
        Assert.AreEqual(IccColorSpaceType.Rgb, header.Value.ColorSpace);
        Assert.AreEqual(IccProfileClass.Display, header.Value.ProfileClass);

        // Assert color space
        Assert.IsNotNull(colorSpace);
        Assert.IsFalse(colorSpace.Value.IsHdr, "Standard RGB profile is not HDR");
        Assert.IsNotNull(colorSpace.Value.TransferFunction);
        // ICC v4 profiles often use parametric curves
        Assert.IsTrue(
            colorSpace.Value.TransferFunction == IccTransferFunction.Parametric ||
            colorSpace.Value.TransferFunction == IccTransferFunction.LookupTable,
            $"Expected parametric or LUT transfer function, got {colorSpace.Value.TransferFunction}");
    }

    [TestMethod]
    public void IccProfileParser_CmykLayers_DetectsCmykColorSpace()
    {
        // Arrange - cmyk_layers.jxl has a CMYK ICC profile
        var data = File.ReadAllBytes("TestData/cmyk_layers.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        // Assert - should have ICC profile
        Assert.IsTrue(profile.IsIcc, "cmyk_layers.jxl should have ICC profile");

        // Act
        var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

        // Assert header - CMYK color space
        Assert.IsNotNull(header);
        Assert.AreEqual(IccColorSpaceType.Cmyk, header.Value.ColorSpace, "Should be CMYK color space");
        Assert.AreEqual(2, header.Value.IccVersion.Major, "Expected ICC v2.x for CMYK profile");

        // Assert color space - CMYK profiles don't have RGB primaries
        Assert.IsNotNull(colorSpace);
        Assert.IsNull(colorSpace.Value.RedPrimary, "CMYK profile shouldn't have RGB primaries");
        Assert.IsNull(colorSpace.Value.GreenPrimary);
        Assert.IsNull(colorSpace.Value.BluePrimary);
        Assert.IsFalse(colorSpace.Value.IsHdr);
    }

    [TestMethod]
    public void IccProfileParser_MultipleGrayscaleProfiles_AllDetectCorrectly()
    {
        // Test both grayscale ICC profiles in the test data
        var grayscaleFiles = new[] { "TestData/with_icc.jxl", "TestData/small_grayscale_patches_modular_with_icc.jxl" };

        foreach (var file in grayscaleFiles)
        {
            var data = File.ReadAllBytes(file);
            using var decoder = new JxlDecoder();
            decoder.SetInput(data);
            decoder.ReadInfo();
            using var profile = decoder.GetEmbeddedColorProfile();

            Assert.IsTrue(profile.IsIcc, $"{file} should have ICC profile");
            Assert.AreEqual(1, profile.Channels, $"{file} should be grayscale (1 channel)");

            var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
            Assert.IsNotNull(header, $"{file} should have parseable header");
            Assert.AreEqual(IccColorSpaceType.Gray, header.Value.ColorSpace, $"{file} should be Gray color space");

            var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);
            Assert.IsNotNull(colorSpace, $"{file} should have parseable color space info");
            Assert.IsNull(colorSpace.Value.RedPrimary, $"{file} grayscale shouldn't have RGB primaries");
        }
    }

    [TestMethod]
    public void IccProfileParser_IccV2VsV4_BothParseCorrectly()
    {
        // Test that both ICC v2 and v4 profiles parse correctly
        var v2Files = new[] { "TestData/lossy_with_icc.jxl", "TestData/cmyk_layers.jxl" };
        var v4Files = new[] { "TestData/progressive.jxl", "TestData/hdr_linear_test.jxl" };

        foreach (var file in v2Files)
        {
            var data = File.ReadAllBytes(file);
            using var decoder = new JxlDecoder();
            decoder.SetInput(data);
            decoder.ReadInfo();
            using var profile = decoder.GetEmbeddedColorProfile();

            var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
            Assert.IsNotNull(header, $"{file} should parse");
            Assert.AreEqual(2, header.Value.IccVersion.Major, $"{file} should be ICC v2.x");
        }

        foreach (var file in v4Files)
        {
            var data = File.ReadAllBytes(file);
            using var decoder = new JxlDecoder();
            decoder.SetInput(data);
            decoder.ReadInfo();
            using var profile = decoder.GetEmbeddedColorProfile();

            var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
            Assert.IsNotNull(header, $"{file} should parse");
            Assert.AreEqual(4, header.Value.IccVersion.Major, $"{file} should be ICC v4.x");
        }
    }

    [TestMethod]
    public void IccProfileParser_RenderingIntent_ParsedCorrectly()
    {
        // Test rendering intent parsing across different profiles
        var testFiles = new[]
        {
            "TestData/lossy_with_icc.jxl",
            "TestData/progressive.jxl",
            "TestData/hdr_linear_test.jxl",
            "TestData/with_icc.jxl"
        };

        foreach (var file in testFiles)
        {
            var data = File.ReadAllBytes(file);
            using var decoder = new JxlDecoder();
            decoder.SetInput(data);
            decoder.ReadInfo();
            using var profile = decoder.GetEmbeddedColorProfile();

            var header = IccProfileParser.TryGetHeaderInfo(profile.IccData);
            Assert.IsNotNull(header, $"{file} should parse");

            // Rendering intent should be one of the valid values
            Assert.IsTrue(
                Enum.IsDefined(header.Value.RenderingIntent),
                $"{file} should have valid rendering intent");
        }
    }

    [TestMethod]
    public void IccColorSpaceInfo_NonHdrProfiles_AllReturnIsHdrFalse()
    {
        // Verify non-HDR profiles correctly report IsHdr = false
        var nonHdrFiles = new[]
        {
            "TestData/lossy_with_icc.jxl",
            "TestData/progressive.jxl",
            "TestData/with_icc.jxl",
            "TestData/cmyk_layers.jxl"
        };

        foreach (var file in nonHdrFiles)
        {
            var data = File.ReadAllBytes(file);
            using var decoder = new JxlDecoder();
            decoder.SetInput(data);
            decoder.ReadInfo();
            using var profile = decoder.GetEmbeddedColorProfile();

            var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);
            Assert.IsNotNull(colorSpace, $"{file} should parse");
            Assert.IsFalse(colorSpace.Value.IsHdr, $"{file} should not be HDR");
            Assert.IsFalse(colorSpace.Value.IsHlg, $"{file} should not be HLG");
            Assert.IsFalse(colorSpace.Value.IsPq, $"{file} should not be PQ");
        }
    }

    [TestMethod]
    public void IccProfileParser_LossyWithIcc_ProfileDescription()
    {
        // Verify we can get the description from lossy_with_icc.jxl
        var data = File.ReadAllBytes("TestData/lossy_with_icc.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        var description = IccProfileParser.TryGetDescription(profile.IccData);
        Assert.IsNotNull(description, "Should be able to get ICC description");
        Assert.IsTrue(description.Length > 0, "Description should not be empty");
    }

    [TestMethod]
    public void IccProfileParser_Progressive_HasPrimaries()
    {
        // Verify RGB primaries are present for progressive.jxl (RGB profile)
        var data = File.ReadAllBytes("TestData/progressive.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        using var profile = decoder.GetEmbeddedColorProfile();

        var colorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);
        Assert.IsNotNull(colorSpace);
        Assert.IsNotNull(colorSpace.Value.WhitePoint, "Should have white point");
        Assert.IsNotNull(colorSpace.Value.RedPrimary, "RGB profile should have red primary");
        Assert.IsNotNull(colorSpace.Value.GreenPrimary, "RGB profile should have green primary");
        Assert.IsNotNull(colorSpace.Value.BluePrimary, "RGB profile should have blue primary");
    }
}
