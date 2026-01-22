using JpegXL.Net;
using JpegXL.Net.Native;

namespace JpegXL.Net.Tests;

[TestClass]
public class JxlDecoderTests
{
    // JXL codestream signature (0xFF 0x0A)
    private static readonly byte[] JxlCodestreamSignature = { 0xFF, 0x0A };
    
    // JXL container signature (ISOBMFF box)
    private static readonly byte[] JxlContainerSignature = 
    { 
        0x00, 0x00, 0x00, 0x0C, // Box size
        0x4A, 0x58, 0x4C, 0x20, // "JXL "
        0x0D, 0x0A, 0x87, 0x0A  // Magic
    };

    [TestMethod]
    public void CheckSignature_WithCodestreamData_ReturnsCodestream()
    {
        // Arrange
        var data = JxlCodestreamSignature;

        // Act
        var result = JxlImage.CheckSignature(data);

        // Assert
        Assert.AreEqual(JxlrsSignature.Codestream, result);
    }

    [TestMethod]
    public void CheckSignature_WithContainerData_ReturnsContainer()
    {
        // Arrange
        var data = JxlContainerSignature;

        // Act
        var result = JxlImage.CheckSignature(data);

        // Assert
        Assert.AreEqual(JxlrsSignature.Container, result);
    }

    [TestMethod]
    public void CheckSignature_WithInvalidData_ReturnsNotEnoughBytesOrInvalid()
    {
        // Arrange
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        // Act
        var result = JxlImage.CheckSignature(data);

        // Assert
        Assert.IsTrue(result == JxlrsSignature.NotEnoughBytes || result == JxlrsSignature.Invalid);
    }

    [TestMethod]
    public void IsJxl_WithCodestreamData_ReturnsTrue()
    {
        // Arrange
        var data = JxlCodestreamSignature;

        // Act
        var result = JxlImage.IsJxl(data);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsJxl_WithContainerData_ReturnsTrue()
    {
        // Arrange
        var data = JxlContainerSignature;

        // Act
        var result = JxlImage.IsJxl(data);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsJxl_WithInvalidData_ReturnsFalse()
    {
        // Arrange
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header

        // Act
        var result = JxlImage.IsJxl(data);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void JxlrsPixelFormat_Default_HasExpectedValues()
    {
        // Arrange & Act
        var format = JxlrsPixelFormat.Default;

        // Assert
        Assert.AreEqual(JxlrsColorType.Rgba, format.ColorType);
        Assert.AreEqual(JxlrsDataFormat.Uint8, format.DataFormat);
        Assert.AreEqual(JxlrsEndianness.Native, format.Endianness);
    }

    [TestMethod]
    public void JxlException_ContainsStatusCode()
    {
        // Arrange & Act
        var exception = new JxlException(JxlrsStatus.Error, "Test error");

        // Assert
        Assert.AreEqual(JxlrsStatus.Error, exception.Status);
        Assert.AreEqual("Test error", exception.Message);
    }

    [TestMethod]
    public void Decode_3x3SrgbLossless_ReturnsCorrectDimensions()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");

        // Act
        using var image = JxlImage.Decode(data);

        // Assert
        Assert.AreEqual(3, image.Width);
        Assert.AreEqual(3, image.Height);
        Assert.AreEqual(4, image.BytesPerPixel); // RGBA 8-bit = 4 bytes per pixel
        Assert.AreEqual(3 * 3 * 4, image.Pixels.Length); // 3x3 pixels * 4 bytes
    }

    [TestMethod]
    public void Decode_Dice_ReturnsValidImage()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");

        // Act
        using var image = JxlImage.Decode(data);

        // Assert
        Assert.IsTrue(image.Width > 0);
        Assert.IsTrue(image.Height > 0);
        Assert.IsTrue(image.Pixels.Length > 0);
    }

    [TestMethod]
    public void Decode_WithBgraFormat_ReturnsCorrectFormat()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");
        var format = JxlrsPixelFormat.Bgra8;

        // Act
        using var image = JxlImage.Decode(data, format);

        // Assert
        Assert.AreEqual(3, image.Width);
        Assert.AreEqual(3, image.Height);
        Assert.AreEqual(4, image.BytesPerPixel);
    }

    [TestMethod]
    public void JxlDecoder_ReadInfo_ReturnsImageInfo()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/dice.jxl");

        // Act
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        var info = decoder.ReadInfo();

        // Assert
        Assert.IsTrue(info.Width > 0);
        Assert.IsTrue(info.Height > 0);
        Assert.IsTrue(info.BitsPerSample > 0);
    }

    [TestMethod]
    public void IsJxl_WithRealJxlFile_ReturnsTrue()
    {
        // Arrange
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");

        // Act
        var result = JxlImage.IsJxl(data);

        // Assert
        Assert.IsTrue(result);
    }
}
