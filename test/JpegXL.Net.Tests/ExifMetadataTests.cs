using System.IO.Compression;
using JpegXL.Net;

namespace JpegXL.Net.Tests;

[TestClass]
public class ExifMetadataTests
{
    // =========================================
    // ExifDataParser Tests (synthetic data)
    // =========================================

    [TestMethod]
    public void TryParse_ValidMinimalTiff_LittleEndian_ReturnsExifData()
    {
        // Arrange
        var exifData = BuildMinimalExifData(bigEndian: false);

        // Act
        var result = ExifDataParser.TryParse(exifData);

        // Assert
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void TryParse_ValidMinimalTiff_BigEndian_ReturnsExifData()
    {
        // Arrange
        var exifData = BuildMinimalExifData(bigEndian: true);

        // Act
        var result = ExifDataParser.TryParse(exifData);

        // Assert
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void TryParse_InvalidData_ReturnsNull()
    {
        // Arrange
        var invalidData = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        // Act
        var result = ExifDataParser.TryParse(invalidData);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_EmptyData_ReturnsNull()
    {
        // Arrange & Act
        var result = ExifDataParser.TryParse(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_TooShortData_ReturnsNull()
    {
        // Arrange - less than minimum header size
        var shortData = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x49, 0x49 };

        // Act
        var result = ExifDataParser.TryParse(shortData);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_InvalidByteOrderMarker_ReturnsNull()
    {
        // Arrange - invalid byte order (neither II nor MM)
        var data = new List<byte>();
        data.AddRange(new byte[] { 0, 0, 0, 0 }); // TIFF offset
        data.AddRange(new byte[] { 0x58, 0x58 }); // Invalid byte order "XX"
        data.AddRange(new byte[] { 0x2A, 0x00 }); // TIFF magic
        data.AddRange(new byte[] { 8, 0, 0, 0 }); // IFD offset
        data.AddRange(new byte[] { 0, 0 }); // 0 entries
        data.AddRange(new byte[] { 0, 0, 0, 0 }); // Next IFD

        // Act
        var result = ExifDataParser.TryParse(data.ToArray());

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_InvalidTiffMagic_ReturnsNull()
    {
        // Arrange - wrong TIFF magic number (should be 42)
        var data = new List<byte>();
        data.AddRange(new byte[] { 0, 0, 0, 0 }); // TIFF offset
        data.AddRange(new byte[] { 0x49, 0x49 }); // Little endian
        data.AddRange(new byte[] { 0x00, 0x00 }); // Wrong magic (0 instead of 42)
        data.AddRange(new byte[] { 8, 0, 0, 0 }); // IFD offset
        data.AddRange(new byte[] { 0, 0 }); // 0 entries
        data.AddRange(new byte[] { 0, 0, 0, 0 }); // Next IFD

        // Act
        var result = ExifDataParser.TryParse(data.ToArray());

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryGetOrientation_ValidData_ReturnsOrientation()
    {
        // Arrange
        var exifData = BuildExifWithOrientation(3); // Rotate 180

        // Act
        var success = ExifDataParser.TryGetOrientation(exifData, out var orientation);

        // Assert
        Assert.IsTrue(success);
        Assert.AreEqual(JxlOrientation.Rotate180, orientation);
    }

    [TestMethod]
    public void TryGetOrientation_NoOrientationTag_ReturnsFalse()
    {
        // Arrange - minimal EXIF with no orientation tag
        var exifData = BuildMinimalExifData(bigEndian: false);

        // Act
        var success = ExifDataParser.TryGetOrientation(exifData, out var orientation);

        // Assert
        Assert.IsFalse(success);
        Assert.IsNull(orientation);
    }

    [TestMethod]
    public void TryGetMakeModel_NoTags_ReturnsFalse()
    {
        // Arrange
        var exifData = BuildMinimalExifData(bigEndian: false);

        // Act
        var success = ExifDataParser.TryGetMakeModel(exifData, out var make, out var model);

        // Assert
        Assert.IsFalse(success);
        Assert.IsNull(make);
        Assert.IsNull(model);
    }

    [TestMethod]
    public void TryGetDateTime_NoDateTag_ReturnsFalse()
    {
        // Arrange
        var exifData = BuildMinimalExifData(bigEndian: false);

        // Act
        var success = ExifDataParser.TryGetDateTime(exifData, out var dateTime);

        // Assert
        Assert.IsFalse(success);
        Assert.IsNull(dateTime);
    }

    // =========================================
    // JxlDecoder EXIF/XML Tests
    // =========================================

    [TestMethod]
    public void HasExifData_ImageWithoutExif_ReturnsFalse()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsFalse(decoder.Metadata.ExifBoxCount > 0);
        Assert.AreEqual(0, (decoder.Metadata.GetExifBox(0)?.Data.Length ?? 0));
    }

    [TestMethod]
    public void GetExifData_ImageWithoutExif_ReturnsNull()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);

        // Assert
        Assert.IsNull(exif);
    }

    [TestMethod]
    public void HasXmlData_ImageWithoutXml_ReturnsFalse()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsFalse(decoder.Metadata.XmlBoxCount > 0);
        Assert.AreEqual(0, (decoder.Metadata.GetXmlBox(0)?.Data.Length ?? 0));
    }

    [TestMethod]
    public void GetXmlData_ImageWithoutXml_ReturnsNull()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var xml = decoder.Metadata.GetXmlBox(0);

        // Assert
        Assert.IsNull(xml);
    }

    [TestMethod]
    public void GetXmlDataString_ImageWithoutXml_ReturnsNull()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var xmlBox = decoder.Metadata.GetXmlBox(0);
        var xml = xmlBox.HasValue ? System.Text.Encoding.UTF8.GetString(xmlBox.Value.Data) : null;

        // Assert
        Assert.IsNull(xml);
    }

    // =========================================
    // Single Box Tests (with test files)
    // =========================================

    [TestMethod]
    public void HasExifData_ImageWithExif_ReturnsTrue()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsTrue(decoder.Metadata.ExifBoxCount > 0);
        Assert.IsTrue((decoder.Metadata.GetExifBox(0)?.Data.Length ?? 0) > 0);
    }

    [TestMethod]
    public void GetExifData_ImageWithExif_ReturnsValidData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);

        // Assert
        Assert.IsNotNull(exif);
        var description = GetExifImageDescription(exif.Value.Data);
        Assert.AreEqual("Test EXIF content", description);
    }

    [TestMethod]
    public void HasXmlData_ImageWithXml_ReturnsTrue()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_xmp.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsTrue(decoder.Metadata.XmlBoxCount > 0);
        Assert.IsTrue((decoder.Metadata.GetXmlBox(0)?.Data.Length ?? 0) > 0);
    }

    [TestMethod]
    public void GetXmlData_ImageWithXml_ReturnsValidData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_xmp.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var xml = decoder.Metadata.GetXmlBox(0);

        // Assert
        Assert.IsNotNull(xml);
        var description = GetXmpDescription(xml.Value.Data);
        Assert.AreEqual("Test XMP content", description);
    }

    [TestMethod]
    public void GetXmlDataString_ImageWithXml_ReturnsValidString()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_xmp.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var xml = decoder.Metadata.GetXmlBox(0);

        // Assert
        Assert.IsNotNull(xml);
        var xmlString = System.Text.Encoding.UTF8.GetString(xml.Value.Data);
        Assert.IsTrue(xmlString.Contains("xmpmeta"));
        Assert.IsTrue(xmlString.Contains("Test XMP content"));
    }

    [TestMethod]
    public void HasJumbfData_ImageWithJumbf_ReturnsTrue()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_jumbf.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsTrue(decoder.Metadata.JumbfBoxCount > 0);
        Assert.IsTrue((decoder.Metadata.GetJumbfBox(0)?.Data.Length ?? 0) > 0);
    }

    [TestMethod]
    public void GetJumbfData_ImageWithJumbf_ReturnsValidData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_jumbf.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var jumbf = decoder.Metadata.GetJumbfBox(0);

        // Assert
        Assert.IsNotNull(jumbf);
        var testValue = GetJumbfTestValue(jumbf.Value.Data);
        Assert.AreEqual("Test JUMBF content", testValue);
    }

    [TestMethod]
    public void HasJumbfData_ImageWithoutJumbf_ReturnsFalse()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsFalse(decoder.Metadata.JumbfBoxCount > 0);
        Assert.AreEqual(0, (decoder.Metadata.GetJumbfBox(0)?.Data.Length ?? 0));
    }

    [TestMethod]
    public void GetJumbfData_ImageWithoutJumbf_ReturnsNull()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var jumbf = decoder.Metadata.GetJumbfBox(0);

        // Assert
        Assert.IsNull(jumbf);
    }

    // =========================================
    // All Metadata Types Test
    // =========================================

    [TestMethod]
    public void AllMetadataTypes_ImageWithAll_ReturnsAll()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/all_metadata.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - verify all metadata types present with expected content
        Assert.IsTrue(decoder.Metadata.ExifBoxCount > 0, "Should have EXIF");
        Assert.IsTrue(decoder.Metadata.XmlBoxCount > 0, "Should have XML");
        Assert.IsTrue(decoder.Metadata.JumbfBoxCount > 0, "Should have JUMBF");

        Assert.AreEqual("EXIF data", GetExifImageDescription(decoder.Metadata.GetExifBox(0)!.Value.Data));
        Assert.AreEqual("XMP data", GetXmpDescription(decoder.Metadata.GetXmlBox(0)!.Value.Data));
        Assert.AreEqual("JUMBF data", GetJumbfTestValue(decoder.Metadata.GetJumbfBox(0)!.Value.Data));
    }

    // =========================================
    // Capture Options Tests
    // =========================================

    [TestMethod]
    public void CaptureDisabled_ExifNotCaptured()
    {
        // Arrange
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.CaptureExif = false;

        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - EXIF should not be captured
        Assert.IsFalse(decoder.Metadata.ExifBoxCount > 0);
    }

    [TestMethod]
    public void CaptureDisabled_XmlNotCaptured()
    {
        // Arrange
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.CaptureXml = false;

        var data = File.ReadAllBytes("TestData/single_xmp.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - XML should not be captured
        Assert.IsFalse(decoder.Metadata.XmlBoxCount > 0);
    }

    [TestMethod]
    public void CaptureDisabled_JumbfNotCaptured()
    {
        // Arrange
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.CaptureJumbf = false;

        var data = File.ReadAllBytes("TestData/single_jumbf.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - JUMBF should not be captured
        Assert.IsFalse(decoder.Metadata.JumbfBoxCount > 0);
    }

    [TestMethod]
    public void NoCapture_NothingCaptured()
    {
        // Arrange
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture = JxlMetadataCaptureOptions.NoCapture;

        var data = File.ReadAllBytes("TestData/all_metadata.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - Nothing should be captured
        Assert.IsFalse(decoder.Metadata.ExifBoxCount > 0, "EXIF should not be captured");
        Assert.IsFalse(decoder.Metadata.XmlBoxCount > 0, "XML should not be captured");
        Assert.IsFalse(decoder.Metadata.JumbfBoxCount > 0, "JUMBF should not be captured");
    }

    [TestMethod]
    public void SelectiveCapture_OnlyExif_CapturesOnlyExif()
    {
        // Arrange
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture = JxlMetadataCaptureOptions.ExifOnly;

        var data = File.ReadAllBytes("TestData/all_metadata.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.IsTrue(decoder.Metadata.ExifBoxCount > 0, "EXIF should be captured");
        Assert.IsFalse(decoder.Metadata.XmlBoxCount > 0, "XML should not be captured");
        Assert.IsFalse(decoder.Metadata.JumbfBoxCount > 0, "JUMBF should not be captured");
    }

    // =========================================
    // Size Limit Tests
    // =========================================

    [TestMethod]
    public void SizeLimit_ExifExceedsLimit_NotCaptured()
    {
        // Arrange - large_exif.jxl has EXIF > 200 bytes, set limit to 50
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.ExifSizeLimit = 50;

        var data = File.ReadAllBytes("TestData/large_exif.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - EXIF should not be captured because it exceeds limit
        Assert.IsFalse(decoder.Metadata.ExifBoxCount > 0);
    }

    [TestMethod]
    public void SizeLimit_ExifBelowLimit_Captured()
    {
        // Arrange - single_exif.jxl has small EXIF, set generous limit
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.ExifSizeLimit = 10000;

        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - EXIF should be captured
        Assert.IsTrue(decoder.Metadata.ExifBoxCount > 0);
    }

    [TestMethod]
    public void SizeLimit_XmlExceedsLimit_NotCaptured()
    {
        // Arrange - single_xmp.jxl has XML > 300 bytes, set limit to 50
        var options = JxlDecodeOptions.Default;
        options.MetadataCapture.XmlSizeLimit = 50;

        var data = File.ReadAllBytes("TestData/single_xmp.jxl");
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - XML should not be captured because it exceeds limit
        Assert.IsFalse(decoder.Metadata.XmlBoxCount > 0);
    }

    // =========================================
    // Multiple Box Tests
    // =========================================

    [TestMethod]
    public void MultipleExifBoxes_ReturnsAllBoxes()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/multi_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - verify multiple boxes are captured
        Assert.IsTrue(decoder.Metadata.ExifBoxCount > 1,
            $"Expected multiple EXIF boxes, got {decoder.Metadata.ExifBoxCount}");

        // Verify each box has distinct, expected content
        var box0 = decoder.Metadata.GetExifBox(0);
        var box1 = decoder.Metadata.GetExifBox(1);
        Assert.IsNotNull(box0);
        Assert.IsNotNull(box1);
        Assert.AreEqual("First EXIF", GetExifImageDescription(box0.Value.Data));
        Assert.AreEqual("Second EXIF", GetExifImageDescription(box1.Value.Data));
    }

    [TestMethod]
    public void MultipleXmlBoxes_ReturnsAllBoxes()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/multi_xmp.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - verify multiple boxes are captured
        Assert.IsTrue(decoder.Metadata.XmlBoxCount > 1,
            $"Expected multiple XML boxes, got {decoder.Metadata.XmlBoxCount}");

        // Verify each box has distinct, expected content
        var box0 = decoder.Metadata.GetXmlBox(0);
        var box1 = decoder.Metadata.GetXmlBox(1);
        Assert.IsNotNull(box0);
        Assert.IsNotNull(box1);
        Assert.AreEqual("First XMP", GetXmpDescription(box0.Value.Data));
        Assert.AreEqual("Second XMP", GetXmpDescription(box1.Value.Data));
    }

    [TestMethod]
    public void MultipleJumbfBoxes_ReturnsAllBoxes()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/multi_jumbf.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert - verify multiple boxes are captured
        Assert.IsTrue(decoder.Metadata.JumbfBoxCount > 1,
            $"Expected multiple JUMBF boxes, got {decoder.Metadata.JumbfBoxCount}");

        // Verify each box has distinct, expected content
        var box0 = decoder.Metadata.GetJumbfBox(0);
        var box1 = decoder.Metadata.GetJumbfBox(1);
        Assert.IsNotNull(box0);
        Assert.IsNotNull(box1);
        Assert.AreEqual("First JUMBF", GetJumbfTestValue(box0.Value.Data));
        Assert.AreEqual("Second JUMBF", GetJumbfTestValue(box1.Value.Data));
    }

    [TestMethod]
    public void GetExifBox_OutOfRange_ReturnsNull()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act - try to access box beyond count
        var box = decoder.Metadata.GetExifBox(100);

        // Assert
        Assert.IsNull(box);
    }

    [TestMethod]
    public void ExifBoxCount_SingleBox_ReturnsOne()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.AreEqual(1, decoder.Metadata.ExifBoxCount);
    }

    [TestMethod]
    public void ExifBoxCount_NoExif_ReturnsZero()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act & Assert
        Assert.AreEqual(0, decoder.Metadata.ExifBoxCount);
    }

    // =========================================
    // Caching Tests
    // =========================================

    [TestMethod]
    public void GetExifData_MultipleCalls_ReturnsSameContent()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif1 = decoder.Metadata.GetExifBox(0);
        var exif2 = decoder.Metadata.GetExifBox(0);

        // Assert - should return identical data
        Assert.IsNotNull(exif1);
        Assert.IsNotNull(exif2);
        CollectionAssert.AreEqual(exif1.Value.Data, exif2.Value.Data);
    }

    // =========================================
    // ExifRational Tests
    // =========================================

    [TestMethod]
    public void ExifRational_ToFloat_ReturnsCorrectValue()
    {
        // Arrange
        var rational = new ExifRational(1, 4);

        // Act
        var result = rational.ToFloat();

        // Assert
        Assert.AreEqual(0.25f, result, 0.0001f);
    }

    [TestMethod]
    public void ExifRational_ToFloat_ZeroDenominator_ReturnsZero()
    {
        // Arrange
        var rational = new ExifRational(5, 0);

        // Act
        var result = rational.ToFloat();

        // Assert
        Assert.AreEqual(0f, result);
    }

    [TestMethod]
    public void ExifRational_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var rational = new ExifRational(1, 250);

        // Act
        var result = rational.ToString();

        // Assert
        Assert.AreEqual("1/250", result);
    }

    [TestMethod]
    public void ExifSignedRational_ToFloat_NegativeValue_ReturnsCorrectValue()
    {
        // Arrange
        var rational = new ExifSignedRational(-1, 3);

        // Act
        var result = rational.ToFloat();

        // Assert
        Assert.AreEqual(-0.333333f, result, 0.0001f);
    }

    // =========================================
    // ExifGpsCoordinate Tests
    // =========================================

    [TestMethod]
    public void ExifGpsCoordinate_ToDecimalDegrees_North_ReturnsPositive()
    {
        // Arrange - 40° 26' 46" N
        var coord = new ExifGpsCoordinate(
            new ExifRational(40, 1),
            new ExifRational(26, 1),
            new ExifRational(46, 1),
            'N');

        // Act
        var result = coord.ToDecimalDegrees();

        // Assert - 40 + 26/60 + 46/3600 = 40.446111...
        Assert.AreEqual(40.446111, result, 0.0001);
    }

    [TestMethod]
    public void ExifGpsCoordinate_ToDecimalDegrees_South_ReturnsNegative()
    {
        // Arrange - 40° 26' 46" S
        var coord = new ExifGpsCoordinate(
            new ExifRational(40, 1),
            new ExifRational(26, 1),
            new ExifRational(46, 1),
            'S');

        // Act
        var result = coord.ToDecimalDegrees();

        // Assert
        Assert.AreEqual(-40.446111, result, 0.0001);
    }

    [TestMethod]
    public void ExifGpsCoordinate_ToDecimalDegrees_West_ReturnsNegative()
    {
        // Arrange - 74° 0' 21" W
        var coord = new ExifGpsCoordinate(
            new ExifRational(74, 1),
            new ExifRational(0, 1),
            new ExifRational(21, 1),
            'W');

        // Act
        var result = coord.ToDecimalDegrees();

        // Assert - -(74 + 0/60 + 21/3600) = -74.005833...
        Assert.AreEqual(-74.005833, result, 0.0001);
    }

    // =========================================
    // Brotli Compression Tests (brob boxes)
    // =========================================

    [TestMethod]
    public void GetExifBox_BrotliCompressed_ReturnsCompressedData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);

        // Assert
        Assert.IsNotNull(exif);
        Assert.IsTrue(exif.Value.IsBrotliCompressed, "EXIF box should be marked as brotli-compressed");
        Assert.IsTrue(exif.Value.Data.Length > 0, "Should have data");

        // Decompress and validate content
        var decompressed = DecompressBrotli(exif.Value);
        Assert.AreEqual("Test EXIF content", GetExifImageDescription(decompressed));
    }

    [TestMethod]
    public void GetXmlBox_BrotliCompressed_ReturnsCompressedData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_xmp_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var xml = decoder.Metadata.GetXmlBox(0);

        // Assert
        Assert.IsNotNull(xml);
        Assert.IsTrue(xml.Value.IsBrotliCompressed, "XML box should be marked as brotli-compressed");
        Assert.IsTrue(xml.Value.Data.Length > 0, "Should have data");

        // Decompress and validate content
        var decompressed = DecompressBrotli(xml.Value);
        Assert.AreEqual("Test XMP content", GetXmpDescription(decompressed));
    }

    [TestMethod]
    public void GetJumbfBox_BrotliCompressed_ReturnsCompressedData()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_jumbf_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var jumbf = decoder.Metadata.GetJumbfBox(0);

        // Assert
        Assert.IsNotNull(jumbf);
        Assert.IsTrue(jumbf.Value.IsBrotliCompressed, "JUMBF box should be marked as brotli-compressed");
        Assert.IsTrue(jumbf.Value.Data.Length > 0, "Should have data");

        // Decompress and validate content
        var decompressed = DecompressBrotli(jumbf.Value);
        Assert.AreEqual("Test JUMBF content", GetJumbfTestValue(decompressed));
    }

    [TestMethod]
    public void GetExifBox_Uncompressed_ReturnsNotCompressed()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);

        // Assert
        Assert.IsNotNull(exif);
        Assert.IsFalse(exif.Value.IsBrotliCompressed, "Uncompressed EXIF box should not be marked as compressed");
    }

    [TestMethod]
    public void MixedCompression_TracksCorrectly()
    {
        // Arrange - file with uncompressed first, compressed second
        var data = File.ReadAllBytes("TestData/mixed_compression.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var box0 = decoder.Metadata.GetExifBox(0);
        var box1 = decoder.Metadata.GetExifBox(1);

        // Assert
        Assert.IsNotNull(box0);
        Assert.IsNotNull(box1);
        Assert.IsFalse(box0.Value.IsBrotliCompressed, "First box should be uncompressed");
        Assert.IsTrue(box1.Value.IsBrotliCompressed, "Second box should be brotli-compressed");

        // Validate content - uncompressed box directly, compressed box via decompression
        Assert.AreEqual("Uncompressed EXIF", GetExifImageDescription(box0.Value.Data));
        Assert.AreEqual("Compressed EXIF", GetExifImageDescription(DecompressBrotli(box1.Value)));
    }

    [TestMethod]
    public void AllMetadataBrob_AllBoxesCompressed()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/all_metadata_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);
        var xml = decoder.Metadata.GetXmlBox(0);
        var jumbf = decoder.Metadata.GetJumbfBox(0);

        // Assert
        Assert.IsNotNull(exif);
        Assert.IsNotNull(xml);
        Assert.IsNotNull(jumbf);
        Assert.IsTrue(exif.Value.IsBrotliCompressed, "EXIF should be compressed");
        Assert.IsTrue(xml.Value.IsBrotliCompressed, "XML should be compressed");
        Assert.IsTrue(jumbf.Value.IsBrotliCompressed, "JUMBF should be compressed");

        // Decompress and validate content
        Assert.AreEqual("EXIF data", GetExifImageDescription(DecompressBrotli(exif.Value)));
        Assert.AreEqual("XMP data", GetXmpDescription(DecompressBrotli(xml.Value)));
        Assert.AreEqual("JUMBF data", GetJumbfTestValue(DecompressBrotli(jumbf.Value)));
    }

    [TestMethod]
    public void MultipleBrotliExifBoxes_AllMarkedCompressed()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/multi_exif_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var count = decoder.Metadata.ExifBoxCount;
        var box0 = decoder.Metadata.GetExifBox(0);
        var box1 = decoder.Metadata.GetExifBox(1);

        // Assert
        Assert.AreEqual(2, count);
        Assert.IsNotNull(box0);
        Assert.IsNotNull(box1);
        Assert.IsTrue(box0.Value.IsBrotliCompressed, "First box should be compressed");
        Assert.IsTrue(box1.Value.IsBrotliCompressed, "Second box should be compressed");

        // Decompress and validate content
        Assert.AreEqual("First EXIF", GetExifImageDescription(DecompressBrotli(box0.Value)));
        Assert.AreEqual("Second EXIF", GetExifImageDescription(DecompressBrotli(box1.Value)));
    }

    [TestMethod]
    public void GetExifBox_BrotliCompressed_DataCanBeDecompressed()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/single_exif_brob.jxl");
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();

        // Act
        var exif = decoder.Metadata.GetExifBox(0);

        // Assert - the data should be compressed, and should decompress to valid EXIF
        Assert.IsNotNull(exif);
        Assert.IsTrue(exif.Value.IsBrotliCompressed, "Box should be marked as compressed");
        Assert.IsTrue(exif.Value.Data.Length > 0, "Compressed data should not be empty");

        // Decompress and validate content
        var decompressed = DecompressBrotli(exif.Value);
        Assert.IsTrue(decompressed.Length > exif.Value.Data.Length,
            "Decompressed data should be larger than compressed data for this test file");
        Assert.AreEqual("Test EXIF content", GetExifImageDescription(decompressed));
    }

    // =========================================
    // Helper methods
    // =========================================

    /// <summary>
    /// Extracts ImageDescription from EXIF data using ExifDataParser.
    /// </summary>
    private static string? GetExifImageDescription(byte[] exifData)
    {
        var parsed = ExifDataParser.TryParse(exifData);
        return parsed?.ImageDescription;
    }

    /// <summary>
    /// Extracts dc:description from XMP XML.
    /// </summary>
    private static string? GetXmpDescription(byte[] xmlData)
    {
        var xml = System.Text.Encoding.UTF8.GetString(xmlData);
        // Simple regex extraction - XMP format is predictable from our test tool
        var match = System.Text.RegularExpressions.Regex.Match(
            xml, @"<dc:description[^>]*>([^<]+)</dc:description>");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Extracts "test" field value from JUMBF JSON content.
    /// </summary>
    private static string? GetJumbfTestValue(byte[] jumbfData)
    {
        // JUMBF structure: jumd box header + json box with content
        // Find the JSON content starting with '{'
        var str = System.Text.Encoding.UTF8.GetString(jumbfData);
        var jsonStart = str.IndexOf('{');
        if (jsonStart < 0) return null;
        var json = str.Substring(jsonStart);
        var match = System.Text.RegularExpressions.Regex.Match(
            json, @"""test"":\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static byte[] DecompressBrotli(JxlMetadataBox box)
    {
        using var input = new MemoryStream(box.Data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] BuildMinimalExifData(bool bigEndian = false)
    {
        var data = new List<byte>();
        // 4-byte TIFF offset (JXL container prefix)
        data.AddRange(new byte[] { 0, 0, 0, 0 });
        // Byte order marker
        data.AddRange(bigEndian ? new byte[] { 0x4D, 0x4D } : new byte[] { 0x49, 0x49 });
        // TIFF magic (42)
        data.AddRange(bigEndian ? new byte[] { 0x00, 0x2A } : new byte[] { 0x2A, 0x00 });
        // IFD0 offset (8 bytes from TIFF start)
        data.AddRange(bigEndian ? new byte[] { 0, 0, 0, 8 } : new byte[] { 8, 0, 0, 0 });
        // IFD0: 0 entries
        data.AddRange(new byte[] { 0, 0 });
        // Next IFD offset: 0 (none)
        data.AddRange(new byte[] { 0, 0, 0, 0 });
        return data.ToArray();
    }

    private static byte[] BuildExifWithOrientation(ushort orientation)
    {
        var data = new List<byte>();
        // 4-byte TIFF offset (JXL container prefix)
        data.AddRange(new byte[] { 0, 0, 0, 0 });
        // Byte order marker (little endian)
        data.AddRange(new byte[] { 0x49, 0x49 });
        // TIFF magic (42)
        data.AddRange(new byte[] { 0x2A, 0x00 });
        // IFD0 offset (8 bytes from TIFF start)
        data.AddRange(new byte[] { 8, 0, 0, 0 });

        // IFD0: 1 entry
        data.AddRange(new byte[] { 1, 0 });

        // Entry: Orientation tag (0x0112)
        data.AddRange(new byte[] { 0x12, 0x01 }); // Tag
        data.AddRange(new byte[] { 0x03, 0x00 }); // Type = SHORT
        data.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // Count = 1
        data.Add((byte)(orientation & 0xFF)); // Value (low byte)
        data.Add((byte)((orientation >> 8) & 0xFF)); // Value (high byte)
        data.AddRange(new byte[] { 0x00, 0x00 }); // Padding

        // Next IFD offset: 0 (none)
        data.AddRange(new byte[] { 0, 0, 0, 0 });

        return data.ToArray();
    }
}
