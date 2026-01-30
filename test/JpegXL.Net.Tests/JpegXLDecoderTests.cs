using JpegXL.Net;

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

    [TestMethod]
    public void StreamingDecode_WithChunkedInput_DecodesSuccessfully()
    {
        // Arrange
        var fullData = File.ReadAllBytes("TestData/dice.jxl");
        
        using var decoder = new JxlDecoder();
        
        // Act - feed data in chunks and process
        int chunkSize = 1024; // 1KB chunks
        int offset = 0;
        JxlBasicInfo? info = null;
        byte[]? pixels = null;
        bool complete = false;
        int maxIterations = 10000; // Safety limit to prevent infinite loops
        int iterations = 0;

        while (!complete && iterations < maxIterations)
        {
            iterations++;
            var evt = decoder.Process();
            
            switch (evt)
            {
                case JxlDecoderEvent.NeedMoreInput:
                    if (offset >= fullData.Length)
                    {
                        Assert.Fail($"Decoder needs more input but we've sent all data");
                    }
                    int bytesToSend = Math.Min(chunkSize, fullData.Length - offset);
                    decoder.AppendInput(fullData.AsSpan(offset, bytesToSend));
                    offset += bytesToSend;
                    break;
                    
                case JxlDecoderEvent.HaveBasicInfo:
                    info = decoder.GetBasicInfo();
                    break;
                    
                case JxlDecoderEvent.NeedOutputBuffer:
                    pixels = new byte[decoder.GetBufferSize()];
                    
                    // Keep trying to read pixels, feeding more data as needed
                    JxlDecoderEvent pixelEvt;
                    do
                    {
                        pixelEvt = decoder.ReadPixels(pixels);
                        if (pixelEvt == JxlDecoderEvent.NeedMoreInput)
                        {
                            if (offset >= fullData.Length)
                            {
                                Assert.Fail("ReadPixels needs more input but we've sent all data");
                            }
                            int moreBytesToSend = Math.Min(chunkSize, fullData.Length - offset);
                            decoder.AppendInput(fullData.AsSpan(offset, moreBytesToSend));
                            offset += moreBytesToSend;
                        }
                    } while (pixelEvt == JxlDecoderEvent.NeedMoreInput && iterations++ < maxIterations);
                    break;
                    
                case JxlDecoderEvent.FrameComplete:
                    // Continue processing
                    break;
                    
                case JxlDecoderEvent.Complete:
                    complete = true;
                    break;
                    
                case JxlDecoderEvent.HaveFrameHeader:
                    // Continue processing
                    break;
                    
                case JxlDecoderEvent.Error:
                    Assert.Fail($"Decoder encountered an error");
                    break;
            }
        }

        if (!complete)
        {
            Assert.Fail($"Test reached max iterations ({maxIterations}). Last offset: {offset}, data length: {fullData.Length}");
        }

        // Assert
        Assert.IsNotNull(info, "BasicInfo should be available after streaming decode");
        Assert.IsTrue(info.Value.Width > 0);
        Assert.IsTrue(info.Value.Height > 0);
        Assert.IsNotNull(pixels, "Pixels should be decoded");
        Assert.IsTrue(pixels.Length > 0);
        Assert.IsTrue(complete, "Decode should complete");
    }

    [TestMethod]
    public void StreamingDecode_HasMoreFrames_ReturnsFalseAfterDecodingStaticImage()
    {
        // Arrange - use a simple static image
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");
        
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        
        // Before decoding, has_more_frames should be true (we haven't decoded any frames yet)
        decoder.ReadInfo();
        Assert.IsTrue(decoder.HasMoreFrames(), "Should have frames to decode before starting");
        
        // Decode the image
        var pixels = decoder.GetPixels();
        
        // After decoding a static (non-animated) image, should have no more frames
        Assert.IsFalse(decoder.HasMoreFrames(), "Should have no more frames after decoding static image");
    }

    [TestMethod]
    public void AnimatedDecode_DecodesMultipleFrames()
    {
        // Arrange - use animated test file
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");
        
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        
        // Get image info first
        var info = decoder.ReadInfo();
        Assert.IsTrue(info.IsAnimated, "Test file should be animated");
        Assert.IsTrue(info.AnimationTpsNumerator > 0, "Should have animation TPS");
        
        // Decode frames
        var frameCount = 0;
        var frameDurations = new List<float>();
        var bytesPerPixel = 4; // RGBA
        var bufferSize = (int)(info.Width * info.Height) * bytesPerPixel;
        var pixels = new byte[bufferSize];
        
        while (decoder.HasMoreFrames())
        {
            // Process until we get frame header
            var evt = decoder.Process();
            while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
            {
                evt = decoder.Process();
            }
            
            if (evt == JxlDecoderEvent.Complete)
                break;
                
            Assert.AreEqual(JxlDecoderEvent.HaveFrameHeader, evt, $"Expected HaveFrameHeader, got {evt}");
            
            // Get frame header
            var frameHeader = decoder.GetFrameHeader();
            frameDurations.Add(frameHeader.DurationMs);
            
            // Process until we need output buffer
            evt = decoder.Process();
            Assert.AreEqual(JxlDecoderEvent.NeedOutputBuffer, evt, $"Expected NeedOutputBuffer, got {evt}");
            
            // Decode pixels
            evt = decoder.ReadPixels(pixels);
            Assert.AreEqual(JxlDecoderEvent.FrameComplete, evt, $"Expected FrameComplete, got {evt}");
            
            frameCount++;
            
            // Safety check to prevent infinite loops
            if (frameCount > 1000)
            {
                Assert.Fail("Too many frames - possible infinite loop");
            }
        }
        
        // Assert - animation_spline.jxl should have multiple frames
        Assert.IsTrue(frameCount > 1, $"Expected multiple frames, got {frameCount}");
        Assert.AreEqual(frameCount, frameDurations.Count, "Should have duration for each frame");
        Console.WriteLine($"Decoded {frameCount} frames with durations: [{string.Join(", ", frameDurations.Select(d => $"{d:F3}s"))}]");
    }

    [TestMethod]
    public void ExtraChannelDecode_ReadsExtraChannelsInfo()
    {
        // Arrange - use file with extra channels
        var data = File.ReadAllBytes("TestData/extra_channels.jxl");
        
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        
        // Get image info
        var info = decoder.ReadInfo();
        
        // Assert - file should have extra channels
        Assert.IsTrue(info.NumExtraChannels > 0, $"Expected extra channels, got {info.NumExtraChannels}");
        
        // Get extra channel info
        for (int i = 0; i < (int)info.NumExtraChannels; i++)
        {
            var channelInfo = decoder.GetExtraChannelInfo(i);
            Console.WriteLine($"Extra channel {i}: Type={channelInfo.ChannelType}, BitsPerSample={channelInfo.BitsPerSample}");
            Assert.IsTrue(channelInfo.BitsPerSample > 0, $"Channel {i} should have valid bits per sample");
        }
    }

    [TestMethod]
    public void ExtraChannelDecode_DecodesExtraChannelsToSeparateBuffers()
    {
        // Arrange - use file with extra channels (alpha)
        // Note: When using RGBA color format (the default), alpha is included in the 
        // 4-channel color output. Any additional extra channels (depth, spot color, etc.)
        // would go to separate buffers. For a file with only alpha as extra channel,
        // when using RGBA there are no channels that need separate buffers.
        var data = File.ReadAllBytes("TestData/extra_channels.jxl");
        
        // Create decoder with extra channel decoding enabled
        var options = new JxlDecodeOptions { DecodeExtraChannels = true };
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        
        // Get image info
        var info = decoder.ReadInfo();
        Assert.IsTrue(info.NumExtraChannels > 0, "Test file should have extra channels");
        
        // Get extra channel info to understand what we have
        var extraChannelTypes = new List<JxlExtraChannelType>();
        for (int i = 0; i < (int)info.NumExtraChannels; i++)
        {
            var channelInfo = decoder.GetExtraChannelInfo(i);
            extraChannelTypes.Add(channelInfo.ChannelType);
            Console.WriteLine($"Extra channel {i}: Type={channelInfo.ChannelType}");
        }
        
        // Process until we need output buffer
        var evt = decoder.Process();
        while (evt != JxlDecoderEvent.NeedOutputBuffer && evt != JxlDecoderEvent.Complete)
        {
            evt = decoder.Process();
        }
        
        Assert.AreEqual(JxlDecoderEvent.NeedOutputBuffer, evt, "Should need output buffer");
        
        // Prepare color buffer
        var colorBufferSize = decoder.GetBufferSize();
        var colorBuffer = new byte[colorBufferSize];
        
        // When using RGBA, the first alpha extra channel is included in the color buffer.
        // Only non-alpha extra channels (or additional alpha channels beyond the first)
        // would need separate buffers.
        // For this test file with only 1 alpha channel, we don't need extra buffers.
        var numNonAlphaExtras = extraChannelTypes.Count(t => t != JxlExtraChannelType.Alpha);
        var numAlphaExtras = extraChannelTypes.Count(t => t == JxlExtraChannelType.Alpha);
        
        Console.WriteLine($"Extra channels: {extraChannelTypes.Count} total, {numAlphaExtras} alpha, {numNonAlphaExtras} non-alpha");
        
        // For images with only alpha extra channels (which are included in RGBA output),
        // we can call ReadPixelsWithExtraChannels with an empty extra buffer array
        var emptyExtraBuffers = Array.Empty<byte[]>();
        Span<byte[]?> extraBuffersSpan = emptyExtraBuffers!;
        evt = decoder.ReadPixelsWithExtraChannels(colorBuffer, extraBuffersSpan);
        Assert.AreEqual(JxlDecoderEvent.FrameComplete, evt, $"Expected FrameComplete, got {evt}");
        
        // Verify color buffer has data (should be RGBA with alpha included)
        bool hasColorData = false;
        for (int i = 0; i < colorBuffer.Length; i++)
        {
            if (colorBuffer[i] != 0)
            {
                hasColorData = true;
                break;
            }
        }
        Assert.IsTrue(hasColorData, "Color buffer should contain non-zero pixel data");
        
        // Verify that the color buffer has 4 channels (RGBA)
        // The alpha data should be in the color buffer's 4th channel
        Assert.AreEqual((int)(info.Width * info.Height * 4), colorBuffer.Length, "Color buffer should be RGBA (4 bytes per pixel)");
        
        Console.WriteLine($"Decoded image {info.Width}x{info.Height} with alpha in RGBA color buffer");
    }
}
