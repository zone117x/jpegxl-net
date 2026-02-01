# HDR Display Notes for macOS

## Overview

This document describes how to correctly display HDR content (HLG and PQ/HDR10) on macOS using Metal and the system tone mapper.

## Key Principle

**Let macOS handle HDR tone mapping** - don't manually linearize and scale HDR content. The system tone mapper (via `CAEdrMetadata`) produces correct results including proper black levels and highlight handling.

## Implementation

### For HLG Content

1. **Set decoder output to native HLG encoding** (not linear):
   ```csharp
   var outputProfile = JxlColorProfile.FromEncoding(
       JxlProfileType.Rgb,
       whitePoint: JxlWhitePointType.D65,
       primaries: JxlPrimariesType.Bt2100,
       transferFunction: JxlTransferFunctionType.Hlg);
   decoder.SetOutputColorProfile(outputProfile);
   ```

2. **Configure CAMetalLayer for HLG**:
   ```csharp
   metalLayer.WantsExtendedDynamicRangeContent = true;
   metalLayer.PixelFormat = MTLPixelFormat.RGBA16Float;
   metalLayer.ColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.Itur_2100_Hlg);
   metalLayer.EdrMetadata = CAEdrMetadata.HlgMetadata;
   ```

### For PQ/HDR10 Content

1. **Set decoder output to native PQ encoding**:
   ```csharp
   var outputProfile = JxlColorProfile.FromEncoding(
       JxlProfileType.Rgb,
       whitePoint: JxlWhitePointType.D65,
       primaries: JxlPrimariesType.Bt2100,
       transferFunction: JxlTransferFunctionType.Pq);
   decoder.SetOutputColorProfile(outputProfile);
   ```

2. **Configure CAMetalLayer for PQ**:
   ```csharp
   metalLayer.WantsExtendedDynamicRangeContent = true;
   metalLayer.PixelFormat = MTLPixelFormat.RGBA16Float;
   metalLayer.ColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.Itur_2100_PQ);
   metalLayer.EdrMetadata = CAEdrMetadata.GetHdr10Metadata(
       minLuminance: 0f,
       maxLuminance: intensityTarget,  // e.g., 1000 or 10000 nits
       opticalOutputScale: 100f);      // SDR reference white in nits
   ```

### For SDR or Other HDR Content

Use extended linear Display P3 with manual brightness scaling:

```csharp
metalLayer.ColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.ExtendedLinearDisplayP3);
metalLayer.EdrMetadata = null;  // No system tone mapping

// In shader: scale by brightness for EDR headroom
const float SdrReferenceWhiteNits = 203f;
var brightnessScale = Math.Min(intensityTarget / SdrReferenceWhiteNits, edrHeadroom);
```

## What We Learned

### The Wrong Approach (caused gray blacks)

1. Convert HLG/PQ to linear in the decoder
2. Use `ExtendedLinearDisplayP3` color space
3. Manually scale brightness by `IntensityTarget / 203`
4. No `CAEdrMetadata`

**Result**: Gray blacks, incorrect tone mapping, clipped highlights.

### Why Native Encoding Works

- HLG and PQ transfer functions encode black as 0.0
- When the decoder converts to linear, small numerical differences can occur
- The system tone mapper expects native HLG/PQ values and handles them correctly
- `CAEdrMetadata` tells macOS how to interpret the pixel values

### EDR Headroom

Query the display's HDR capability:
```csharp
var edrHeadroom = screen.MaximumExtendedDynamicRangeColorComponentValue;
// 1.0 = SDR only, >1.0 = HDR supported (e.g., 4.0x on XDR displays)
```

Headroom varies based on display brightness - it's lower when the display is at maximum brightness.

## Debugging Tips

### Check Pixel Values

Sample decoded pixel values to verify encoding:
```csharp
// True black should be 0.0, HDR white should be 1.0
Console.WriteLine($"Pixel: R={r:F4} G={g:F4} B={b:F4}");
```

### Verify Color Profile Detection

```csharp
using var profile = decoder.GetEmbeddedColorProfile();
Console.WriteLine($"IsHlg={profile.IsHlg}, IsPq={profile.IsPq}");
Console.WriteLine($"Description: {profile.GetDescription()}");
```

### Common Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Gray blacks | Decoder linearizing content | Set output profile to native HLG/PQ |
| Dim HDR | Wrong color space on layer | Use Itur_2100_Hlg or Itur_2100_PQ |
| Clipped highlights | No EDR metadata | Set CAEdrMetadata.HlgMetadata or GetHdr10Metadata |
| Wrong colors | Mismatched primaries | Use Bt2100 primaries for Rec.2020 content |

## References

- [WWDC21: Explore HDR rendering with EDR](https://developer.apple.com/videos/play/wwdc2021/10161/)
- [WWDC22: Explore EDR on iOS](https://developer.apple.com/videos/play/wwdc2022/10113/)
- [WWDC22: Display HDR video in EDR](https://developer.apple.com/videos/play/wwdc2022/110565/)
- [Apple Docs: Displaying HDR content in a Metal layer](https://developer.apple.com/documentation/metal/displaying-hdr-content-in-a-metal-layer)
- [Metal by Example: Rendering HDR Video](https://metalbyexample.com/hdr-video/)
