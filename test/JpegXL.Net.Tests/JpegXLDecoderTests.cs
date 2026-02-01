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

        // Assert - dice.jxl has known dimensions
        Assert.AreEqual(800, image.Width, "dice.jxl should be 800 pixels wide");
        Assert.AreEqual(600, image.Height, "dice.jxl should be 600 pixels tall");
        Assert.AreEqual(4, image.BytesPerPixel, "Default format is RGBA8 (4 bytes per pixel)");
        Assert.AreEqual(800 * 600 * 4, image.Pixels.Length, "Pixel buffer size should match dimensions");
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

        // Assert - dice.jxl has known properties
        Assert.AreEqual(800u, info.Size.Width, "dice.jxl should be 800 pixels wide");
        Assert.AreEqual(600u, info.Size.Height, "dice.jxl should be 600 pixels tall");
        Assert.AreEqual(8u, info.BitDepth.BitsPerSample, "dice.jxl should be 8-bit");
        Assert.IsFalse(info.IsAnimated, "dice.jxl should not be animated");
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
        Assert.IsTrue(info.Size.Width > 0);
        Assert.IsTrue(info.Size.Height > 0);
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
        Assert.IsTrue(info.Animation!.Value.TpsNumerator > 0, "Should have animation TPS");

        // Decode frames
        var frameCount = 0;
        var frameDurations = new List<float>();
        var bytesPerPixel = 4; // RGBA
        var bufferSize = (int)info.Size.Width * (int)info.Size.Height * bytesPerPixel;
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

        // All frame durations should be positive for an animated image
        Assert.IsTrue(frameDurations.All(d => d > 0), "All frame durations should be positive");
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
        Assert.IsTrue(info.ExtraChannels.Count > 0, $"Expected extra channels, got {info.ExtraChannels.Count}");

        // Get extra channel info and verify types are valid
        for (int i = 0; i < info.ExtraChannels.Count; i++)
        {
            var channelInfo = decoder.GetExtraChannelInfo(i);
            // Verify channel type is a defined enum value
            Assert.IsTrue(Enum.IsDefined(channelInfo.ChannelType),
                $"Extra channel {i} should have a valid channel type");
        }

        // First extra channel should be Alpha for this test file
        var firstChannel = decoder.GetExtraChannelInfo(0);
        Assert.AreEqual(JxlExtraChannelType.Alpha, firstChannel.ChannelType,
            "First extra channel should be Alpha");
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
        Assert.IsTrue(info.ExtraChannels.Count > 0, "Test file should have extra channels");

        // Get extra channel info to understand what we have
        var extraChannelTypes = new List<JxlExtraChannelType>();
        for (int i = 0; i < info.ExtraChannels.Count; i++)
        {
            var channelInfo = decoder.GetExtraChannelInfo(i);
            extraChannelTypes.Add(channelInfo.ChannelType);
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
        
        // Verify channel type counts
        Assert.IsTrue(numAlphaExtras >= 1, "Should have at least one alpha channel");
        
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
        Assert.AreEqual((int)info.Size.Width * (int)info.Size.Height * 4, colorBuffer.Length, "Color buffer should be RGBA (4 bytes per pixel)");

        // Verify alpha channel has meaningful data (not all 0 or all 255)
        bool hasVariedAlpha = false;
        byte firstAlpha = colorBuffer[3];
        for (int i = 3; i < colorBuffer.Length; i += 4)
        {
            if (colorBuffer[i] != firstAlpha)
            {
                hasVariedAlpha = true;
                break;
            }
        }
        Assert.IsTrue(hasVariedAlpha || firstAlpha != 0, "Alpha channel should have meaningful data");
    }

    [TestMethod]
    public void ParseFrameMetadata_ReturnsAnimationInfo()
    {
        // Arrange - use animated test file
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var metadata = decoder.ParseFrameMetadata();
        var basicInfo = decoder.BasicInfo;

        // Assert
        Assert.IsNotNull(basicInfo, "BasicInfo should be available after ParseFrameMetadata");
        Assert.IsTrue(basicInfo.IsAnimated, "Should be animated");
        Assert.IsTrue(metadata.FrameCount > 1, $"Expected multiple frames, got {metadata.FrameCount}");
        Assert.AreEqual(metadata.Frames.Count, metadata.FrameCount, "FrameCount should match Frames.Count");
        Assert.IsTrue(metadata.GetTotalDurationMs() > 0, "Should have positive total duration");
        Assert.IsNull(metadata.FrameNames, "FrameNames should be null when includeNames is false");

        // All frames should have valid dimensions
        foreach (var frame in metadata.Frames)
        {
            Assert.IsTrue(frame.FrameWidth > 0, "Frame should have valid width");
            Assert.IsTrue(frame.FrameHeight > 0, "Frame should have valid height");
        }

        // Verify frame durations are consistent (all should be equal for this test file)
        var firstDuration = metadata.Frames[0].DurationMs;
        Assert.IsTrue(metadata.Frames.All(f => Math.Abs(f.DurationMs - firstDuration) < 0.01f),
            "All frames should have equal duration in animation_spline.jxl");
    }

    [TestMethod]
    public void ParseFrameMetadata_StaticImage_ReturnsSingleFrame()
    {
        // Arrange - use static test file
        var data = File.ReadAllBytes("TestData/dice.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var metadata = decoder.ParseFrameMetadata();
        var basicInfo = decoder.BasicInfo;

        // Assert
        Assert.IsNotNull(basicInfo, "BasicInfo should be available after ParseFrameMetadata");
        Assert.IsFalse(basicInfo.IsAnimated, "Should not be animated");
        Assert.AreEqual(1, metadata.FrameCount, "Static image should have 1 frame");
        Assert.AreEqual(0, metadata.Frames[0].DurationMs, "Static image frame should have 0 duration");

        // Verify dimensions are valid for dice.jxl
        Assert.IsTrue(basicInfo.Size.Width > 0 && basicInfo.Size.Height > 0,
            "Should have valid dimensions");
    }

    [TestMethod]
    public void ParseFrameMetadata_MaxFramesLimit_ThrowsWhenExceeded()
    {
        // Arrange - use animated test file
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act & Assert - limit to 2 frames should throw since animation has more
        var ex = Assert.ThrowsException<JxlException>(() => decoder.ParseFrameMetadata(maxFrames: 2));
        Assert.IsTrue(ex.Message.Contains("exceeded limit"), $"Expected limit exceeded message, got: {ex.Message}");
    }

    [TestMethod]
    public void ToneMapping_HdrPqFile_HasHighIntensityTarget()
    {
        // Arrange - HDR PQ (Perceptual Quantizer) test file
        var data = File.ReadAllBytes("TestData/hdr_pq_test.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var info = decoder.ReadInfo();

        // Assert - HDR files should have IntensityTarget > 255 nits (SDR max)
        Assert.IsTrue(info.ToneMapping.IntensityTarget > 255f,
            $"HDR PQ file should have IntensityTarget > 255, got {info.ToneMapping.IntensityTarget}");
        Assert.IsTrue(info.IsHdr, "IsHdr should be true for HDR PQ file");

        // PQ content typically has intensity target in range 1000-10000 nits
        Assert.IsTrue(info.ToneMapping.IntensityTarget >= 1000f && info.ToneMapping.IntensityTarget <= 10000f,
            $"HDR PQ IntensityTarget should be in typical PQ range (1000-10000), got {info.ToneMapping.IntensityTarget}");

        // MinNits should be non-negative
        Assert.IsTrue(info.ToneMapping.MinNits >= 0f,
            $"MinNits should be non-negative, got {info.ToneMapping.MinNits}");
    }

    [TestMethod]
    public void ToneMapping_HdrHlgFile_HasHighIntensityTarget()
    {
        // Arrange - HDR HLG (Hybrid Log-Gamma) test file
        var data = File.ReadAllBytes("TestData/hdr_hlg_test.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var info = decoder.ReadInfo();

        // Assert - HDR files should have IntensityTarget > 255 nits (SDR max)
        Assert.IsTrue(info.ToneMapping.IntensityTarget > 255f,
            $"HDR HLG file should have IntensityTarget > 255, got {info.ToneMapping.IntensityTarget}");
        Assert.IsTrue(info.IsHdr, "IsHdr should be true for HDR HLG file");

        // HLG typically uses 1000 nits as reference
        Assert.IsTrue(info.ToneMapping.IntensityTarget >= 1000f,
            $"HDR HLG IntensityTarget should be >= 1000 nits, got {info.ToneMapping.IntensityTarget}");

        // MinNits should be non-negative
        Assert.IsTrue(info.ToneMapping.MinNits >= 0f,
            $"MinNits should be non-negative, got {info.ToneMapping.MinNits}");
    }

    [TestMethod]
    public void ToneMapping_SdrFile_HasStandardIntensityTarget()
    {
        // Arrange - Standard SDR file
        var data = File.ReadAllBytes("TestData/dice.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var info = decoder.ReadInfo();

        // Assert - SDR files should have IntensityTarget <= 255 nits
        Assert.IsTrue(info.ToneMapping.IntensityTarget <= 255f,
            $"SDR file should have IntensityTarget <= 255, got {info.ToneMapping.IntensityTarget}");
        Assert.IsFalse(info.IsHdr, "IsHdr should be false for SDR file");

        // SDR typically uses ~80-100 nits
        Assert.IsTrue(info.ToneMapping.IntensityTarget >= 0f,
            $"IntensityTarget should be non-negative, got {info.ToneMapping.IntensityTarget}");
    }

    [TestMethod]
    public void ExtraChannel_SpotFile_HasSpotColorChannelType()
    {
        // Arrange - Spot color test file from jxl-rs conformance tests
        var data = File.ReadAllBytes("TestData/spot.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var info = decoder.ReadInfo();

        // Assert - File should have extra channels including spot color
        Assert.IsTrue(info.ExtraChannels.Count > 0,
            $"Spot file should have extra channels, got {info.ExtraChannels.Count}");

        // Find spot color channel(s) by type
        var spotChannelCount = 0;
        for (int i = 0; i < info.ExtraChannels.Count; i++)
        {
            var channelInfo = decoder.GetExtraChannelInfo(i);
            if (channelInfo.ChannelType == JxlExtraChannelType.SpotColor)
            {
                spotChannelCount++;
            }
        }

        // Note: jxl-rs API doesn't expose spot color RGBA values, only the channel type
        Assert.IsTrue(spotChannelCount > 0, "Spot file should have at least one SpotColor channel type");
    }

    [TestMethod]
    public void BasicInfo_NewFields_ArePopulated()
    {
        // Arrange - Use a file with various metadata
        var data = File.ReadAllBytes("TestData/dice.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act
        var info = decoder.ReadInfo();

        // Assert - Fields should have reasonable values
        // AlphaPremultiplied should be false for standard images
        Assert.IsFalse(info.AlphaPremultiplied, "dice.jxl should not have premultiplied alpha");

        // ToneMapping struct should have valid values
        Assert.IsTrue(info.ToneMapping.IntensityTarget >= 0f, "IntensityTarget should be non-negative");
        Assert.IsTrue(info.ToneMapping.MinNits >= 0f, "MinNits should be non-negative");
    }

    // =========================================================================
    // FFI Alpha Channel Handling Tests
    // =========================================================================

    [TestMethod]
    public void PixelFormat_RgbaWithAlphaChannel_IncludesAlphaInMainBuffer()
    {
        // When using RGBA format, alpha should be in the 4th channel of the main buffer
        var data = File.ReadAllBytes("TestData/dice.jxl"); // Has alpha channel

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        var info = decoder.ReadInfo();

        // dice.jxl should have an alpha extra channel
        Assert.IsTrue(info.ExtraChannels.Count >= 1, "dice.jxl should have extra channels");
        var hasAlpha = false;
        for (int i = 0; i < info.ExtraChannels.Count; i++)
        {
            if (decoder.GetExtraChannelInfo(i).ChannelType == JxlExtraChannelType.Alpha)
            {
                hasAlpha = true;
                break;
            }
        }
        Assert.IsTrue(hasAlpha, "dice.jxl should have an alpha channel");

        // Decode with RGBA format (default)
        var pixels = decoder.GetPixels();
        Assert.AreEqual((int)info.Size.Width * (int)info.Size.Height * 4, pixels.Length,
            "RGBA decode should produce 4 bytes per pixel");

        // Verify alpha channel has data (not all zeros)
        bool hasAlphaData = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlphaData = true;
                break;
            }
        }
        Assert.IsTrue(hasAlphaData, "Alpha channel should contain non-zero data");
    }

    [TestMethod]
    public void PixelFormat_RgbWithAlphaChannel_ExcludesAlphaFromMainBuffer()
    {
        // When using RGB format (no alpha), output should be 3 bytes per pixel
        var data = File.ReadAllBytes("TestData/dice.jxl"); // Has alpha channel

        var format = new JxlPixelFormat
        {
            ColorType = JxlColorType.Rgb,
            DataFormat = JxlDataFormat.Uint8,
            Endianness = JxlEndianness.Native
        };

        using var decoder = new JxlDecoder();
        decoder.SetPixelFormat(format);
        decoder.SetInput(data);
        var info = decoder.ReadInfo();

        // Decode with RGB format
        var pixels = decoder.GetPixels();
        Assert.AreEqual((int)info.Size.Width * (int)info.Size.Height * 3, pixels.Length,
            "RGB decode should produce 3 bytes per pixel");
    }

    // =========================================================================
    // Streaming Robustness Tests
    // =========================================================================

    [TestMethod]
    [DataRow(1, DisplayName = "1-byte chunks (extreme)")]
    [DataRow(7, DisplayName = "7-byte chunks (prime)")]
    [DataRow(64, DisplayName = "64-byte chunks")]
    [DataRow(256, DisplayName = "256-byte chunks")]
    [DataRow(1024, DisplayName = "1KB chunks")]
    public void StreamingDecode_WithVariableChunkSizes_ProducesValidOutput(int chunkSize)
    {
        // Streaming decode with various chunk sizes should all work correctly
        var fullData = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");

        using var decoder = new JxlDecoder();

        int offset = 0;
        JxlBasicInfo? info = null;
        byte[]? pixels = null;
        bool complete = false;
        int maxIterations = 100000;
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
                        Assert.Fail("Decoder needs more input but all data sent");
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
                    JxlDecoderEvent pixelEvt;
                    do
                    {
                        pixelEvt = decoder.ReadPixels(pixels);
                        if (pixelEvt == JxlDecoderEvent.NeedMoreInput)
                        {
                            if (offset >= fullData.Length)
                            {
                                Assert.Fail("ReadPixels needs more input but all data sent");
                            }
                            int more = Math.Min(chunkSize, fullData.Length - offset);
                            decoder.AppendInput(fullData.AsSpan(offset, more));
                            offset += more;
                        }
                    } while (pixelEvt == JxlDecoderEvent.NeedMoreInput && iterations++ < maxIterations);
                    break;

                case JxlDecoderEvent.FrameComplete:
                case JxlDecoderEvent.HaveFrameHeader:
                    break;

                case JxlDecoderEvent.Complete:
                    complete = true;
                    break;

                case JxlDecoderEvent.Error:
                    Assert.Fail("Decoder error");
                    break;
            }
        }

        Assert.IsTrue(complete, $"Decode should complete with {chunkSize}-byte chunks");
        Assert.IsNotNull(info);
        Assert.AreEqual(3u, info.Size.Width);
        Assert.AreEqual(3u, info.Size.Height);
        Assert.IsNotNull(pixels);
        Assert.AreEqual(3 * 3 * 4, pixels.Length);
    }

    [TestMethod]
    public void Decode_StreamingVsOneShot_ProducesIdenticalPixels()
    {
        // Critical FFI test: streaming API must produce byte-identical output to one-shot decode
        var data = File.ReadAllBytes("TestData/3x3_srgb_lossless.jxl");

        // One-shot decode (reference)
        using var oneShotImage = JxlImage.Decode(data);
        var referencePixels = oneShotImage.GetPixelArray();

        // Streaming decode
        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        decoder.ReadInfo();
        var streamingPixels = decoder.GetPixels();

        // Must be byte-identical
        Assert.AreEqual(referencePixels.Length, streamingPixels.Length,
            "Streaming and one-shot should produce same buffer size");

        for (int i = 0; i < referencePixels.Length; i++)
        {
            Assert.AreEqual(referencePixels[i], streamingPixels[i],
                $"Pixel byte mismatch at index {i}: one-shot={referencePixels[i]}, streaming={streamingPixels[i]}");
        }
    }

    // =========================================================================
    // Signature Detection Edge Cases
    // =========================================================================

    [TestMethod]
    public void CheckSignature_PartialContainer_ReturnsNotEnoughBytes()
    {
        // Only first 6 bytes of container signature (needs 12)
        var partialData = new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58 };

        var result = JxlImage.CheckSignature(partialData);

        Assert.AreEqual(JxlSignature.NotEnoughBytes, result,
            "Partial container signature should return NotEnoughBytes");
    }

    [TestMethod]
    public void CheckSignature_PartialCodestream_ReturnsNotEnoughBytes()
    {
        // Only first byte of codestream signature (needs 2)
        var partialData = new byte[] { 0xFF };

        var result = JxlImage.CheckSignature(partialData);

        Assert.AreEqual(JxlSignature.NotEnoughBytes, result,
            "Partial codestream signature should return NotEnoughBytes");
    }

    [TestMethod]
    public void CheckSignature_EmptyBuffer_ReturnsNotEnoughBytes()
    {
        var result = JxlImage.CheckSignature([]);

        Assert.AreEqual(JxlSignature.NotEnoughBytes, result,
            "Empty buffer should return NotEnoughBytes");
    }

    [TestMethod]
    public void CheckSignature_ContainerWithTrailingData_ReturnsContainer()
    {
        // Valid container signature followed by extra bytes
        var dataWithTrailing = new byte[]
        {
            0x00, 0x00, 0x00, 0x0C,
            0x4A, 0x58, 0x4C, 0x20,
            0x0D, 0x0A, 0x87, 0x0A,
            0xFF, 0xFF, 0xFF, 0xFF // trailing data
        };

        var result = JxlImage.CheckSignature(dataWithTrailing);

        Assert.AreEqual(JxlSignature.Container, result,
            "Container signature with trailing data should still return Container");
    }

    // =========================================================================
    // Error Handling Tests
    // =========================================================================

    [TestMethod]
    public void Decode_InvalidSignature_ThrowsJxlException()
    {
        // PNG header (not JXL)
        var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var ex = Assert.ThrowsException<JxlException>(() => JxlImage.Decode(pngData));
        Assert.AreEqual(JxlStatus.Error, ex.Status);
    }

    [TestMethod]
    public void Decode_TruncatedFile_ThrowsJxlException()
    {
        // Read a valid file and truncate it
        var fullData = File.ReadAllBytes("TestData/dice.jxl");
        var truncatedData = fullData[..(fullData.Length / 4)]; // Take only first 25%

        Assert.ThrowsException<JxlException>(() => JxlImage.Decode(truncatedData));
    }

    [TestMethod]
    public void Decode_EmptyFile_ThrowsJxlException()
    {
        Assert.ThrowsException<JxlException>(() => JxlImage.Decode([]));
    }

    [TestMethod]
    public void Decode_CorruptedData_ThrowsJxlException()
    {
        // Take valid file and corrupt the middle
        var data = File.ReadAllBytes("TestData/dice.jxl");
        var corruptedData = (byte[])data.Clone();

        // Corrupt bytes in the middle of the file
        for (int i = data.Length / 2; i < data.Length / 2 + 100 && i < data.Length; i++)
        {
            corruptedData[i] = 0xFF;
        }

        Assert.ThrowsException<JxlException>(() => JxlImage.Decode(corruptedData));
    }

    // =========================================================================
    // Animation Frame Content Validation
    // =========================================================================

    [TestMethod]
    public void AnimatedDecode_FramesDiffer_PixelContentChanges()
    {
        // Verify that animation frames have different pixel content
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);
        var info = decoder.ReadInfo();

        Assert.IsTrue(info.IsAnimated, "Test file should be animated");

        var bufferSize = (int)info.Size.Width * (int)info.Size.Height * 4;
        var frame0Pixels = new byte[bufferSize];
        var frame1Pixels = new byte[bufferSize];

        // Decode frame 0
        var evt = decoder.Process();
        while (evt != JxlDecoderEvent.NeedOutputBuffer && evt != JxlDecoderEvent.Complete)
        {
            evt = decoder.Process();
        }
        if (evt == JxlDecoderEvent.NeedOutputBuffer)
        {
            decoder.ReadPixels(frame0Pixels);
        }

        // Move to next frame
        Assert.IsTrue(decoder.HasMoreFrames(), "Should have more frames after first");

        evt = decoder.Process();
        while (evt != JxlDecoderEvent.NeedOutputBuffer && evt != JxlDecoderEvent.Complete)
        {
            if (evt == JxlDecoderEvent.HaveFrameHeader)
            {
                evt = decoder.Process();
                continue;
            }
            evt = decoder.Process();
        }
        if (evt == JxlDecoderEvent.NeedOutputBuffer)
        {
            decoder.ReadPixels(frame1Pixels);
        }

        // Frames should differ (animation should have changing content)
        bool framesDiffer = false;
        for (int i = 0; i < frame0Pixels.Length; i++)
        {
            if (frame0Pixels[i] != frame1Pixels[i])
            {
                framesDiffer = true;
                break;
            }
        }

        Assert.IsTrue(framesDiffer, "Animation frames should have different pixel content");
    }

    [TestMethod]
    public void ParseFrameMetadata_WithIncludeNames_ReturnsFrameNamesList()
    {
        // Arrange - use animation file with multiple frames
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Act - parse with includeNames = true
        var metadata = decoder.ParseFrameMetadata(includeNames: true);

        // Assert - FrameNames should be populated (even if names are empty strings)
        Assert.IsNotNull(metadata.FrameNames, "FrameNames should not be null when includeNames is true");
        Assert.AreEqual(metadata.FrameCount, metadata.FrameNames.Count,
            "FrameNames count should match FrameCount");

        // Verify each frame name corresponds to its NameLength
        for (int i = 0; i < metadata.Frames.Count; i++)
        {
            var frame = metadata.Frames[i];
            var name = metadata.FrameNames[i];

            if (frame.NameLength == 0)
            {
                Assert.AreEqual(string.Empty, name,
                    $"Frame {i}: name should be empty when NameLength is 0");
            }
            else
            {
                Assert.IsTrue(name.Length > 0,
                    $"Frame {i}: name should be non-empty when NameLength is {frame.NameLength}");
            }
        }
    }

    [TestMethod]
    public void GetFrameName_ReturnsEmptyStringForUnnamedFrame()
    {
        // Arrange - use a file with frames that have no names
        var data = File.ReadAllBytes("TestData/animation_spline.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Read info first
        decoder.ReadInfo();

        // Process until we get the frame header
        var evt = decoder.Process();
        while (evt != JxlDecoderEvent.HaveFrameHeader && evt != JxlDecoderEvent.Complete)
        {
            evt = decoder.Process();
        }

        Assert.AreEqual(JxlDecoderEvent.HaveFrameHeader, evt, "Should receive HaveFrameHeader event");

        // Act - get frame header and name
        var header = decoder.GetFrameHeader();
        var frameName = decoder.GetFrameName();

        // Assert - for unnamed frames, GetFrameName should return empty string
        Assert.AreEqual(0u, header.NameLength, "This test file should have unnamed frames");
        Assert.AreEqual(string.Empty, frameName, "Unnamed frame should return empty string");
    }

    [TestMethod]
    public void GetFrameName_WithNamedFrame_ReturnsFrameName()
    {
        // Arrange - use the test file with a named frame
        var data = File.ReadAllBytes("TestData/named_frame_test.jxl");

        using var decoder = new JxlDecoder();
        decoder.SetInput(data);

        // Read info first
        decoder.ReadInfo();

        // Process until we get the frame header
        var evt = decoder.Process();
        while (evt != JxlDecoderEvent.HaveFrameHeader && evt != JxlDecoderEvent.Complete)
        {
            evt = decoder.Process();
        }

        Assert.AreEqual(JxlDecoderEvent.HaveFrameHeader, evt, "Should receive HaveFrameHeader event");

        // Act - get frame header and name
        var header = decoder.GetFrameHeader();
        Console.WriteLine($"NameLength from header: {header.NameLength}");

        var frameName = decoder.GetFrameName();
        Console.WriteLine($"Frame name: '{frameName}'");

        // Assert - this file should have a named frame
        Assert.AreEqual(13u, header.NameLength, $"Expected NameLength=13, got {header.NameLength}");
        Assert.AreEqual("TestFrameName", frameName, $"Expected 'TestFrameName', got '{frameName}'");
    }
}
