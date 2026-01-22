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
        Assert.AreEqual(JxlSignature.Codestream, result);
    }

    [TestMethod]
    public void CheckSignature_WithContainerData_ReturnsContainer()
    {
        // Arrange
        var data = JxlContainerSignature;

        // Act
        var result = JxlImage.CheckSignature(data);

        // Assert
        Assert.AreEqual(JxlSignature.Container, result);
    }

    [TestMethod]
    public void CheckSignature_WithInvalidData_ReturnsNotEnoughBytesOrInvalid()
    {
        // Arrange
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        // Act
        var result = JxlImage.CheckSignature(data);

        // Assert
        Assert.IsTrue(result == JxlSignature.NotEnoughBytes || result == JxlSignature.Invalid);
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
    public void JxlPixelFormat_Default_HasExpectedValues()
    {
        // Arrange & Act
        var format = JxlPixelFormat.Default;

        // Assert
        Assert.AreEqual(JxlColorType.Rgba, format.ColorType);
        Assert.AreEqual(JxlDataFormat.Uint8, format.DataFormat);
        Assert.AreEqual(JxlEndianness.Native, format.Endianness);
    }

    [TestMethod]
    public void JxlException_ContainsStatusCode()
    {
        // Arrange & Act
        var exception = new JxlException(JxlStatus.Error, "Test error");

        // Assert
        Assert.AreEqual(JxlStatus.Error, exception.Status);
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
        var format = JxlPixelFormat.Bgra8;

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

    [TestMethod]
    public void Decode_WithPremultiplyAlpha_PremultipliesCorrectly()
    {
        // Arrange - dice.jxl has transparency
        var data = File.ReadAllBytes("TestData/dice.jxl");
        var format = JxlPixelFormat.Bgra8;
        var optionsStraight = new JxlDecodeOptions { PremultiplyAlpha = false };
        var optionsPremul = new JxlDecodeOptions { PremultiplyAlpha = true };

        // Act
        using var straightImage = JxlImage.Decode(data, format, optionsStraight);
        using var premulImage = JxlImage.Decode(data, format, optionsPremul);

        var straightPixels = straightImage.GetPixelArray();
        var premulPixels = premulImage.GetPixelArray();

        // Assert - find a semi-transparent pixel and verify premultiplication
        bool foundSemiTransparent = false;
        for (int i = 0; i < straightPixels.Length; i += 4)
        {
            byte b = straightPixels[i];
            byte g = straightPixels[i + 1];
            byte r = straightPixels[i + 2];
            byte a = straightPixels[i + 3];

            // Look for semi-transparent pixels (not fully opaque, not fully transparent)
            if (a > 0 && a < 255 && (r > 0 || g > 0 || b > 0))
            {
                foundSemiTransparent = true;

                byte premulB = premulPixels[i];
                byte premulG = premulPixels[i + 1];
                byte premulR = premulPixels[i + 2];
                byte premulA = premulPixels[i + 3];

                // Alpha should be unchanged
                Assert.AreEqual(a, premulA, $"Alpha mismatch at pixel {i / 4}");

                // Premultiplied values should be: color * alpha / 255
                // Allow ±1 tolerance for rounding
                byte expectedB = (byte)(b * a / 255);
                byte expectedG = (byte)(g * a / 255);
                byte expectedR = (byte)(r * a / 255);

                Assert.IsTrue(Math.Abs(premulB - expectedB) <= 1,
                    $"B mismatch at pixel {i / 4}: expected {expectedB}, got {premulB}");
                Assert.IsTrue(Math.Abs(premulG - expectedG) <= 1,
                    $"G mismatch at pixel {i / 4}: expected {expectedG}, got {premulG}");
                Assert.IsTrue(Math.Abs(premulR - expectedR) <= 1,
                    $"R mismatch at pixel {i / 4}: expected {expectedR}, got {premulR}");

                break; // Found one, that's enough
            }
        }

        Assert.IsTrue(foundSemiTransparent, "No semi-transparent pixels found in dice.jxl");
    }
}
