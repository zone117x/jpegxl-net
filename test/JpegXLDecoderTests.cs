using JpegXL.Net;

namespace JpegXL.Net.Tests;

[TestClass]
public class JpegXLDecoderTests
{
    [TestMethod]
    public void Constructor_ShouldCreateInstance()
    {
        // Arrange & Act
        var decoder = new JpegXLDecoder();

        // Assert
        Assert.IsNotNull(decoder);
    }
}
