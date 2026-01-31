// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! C-compatible type definitions.

/// Opaque decoder handle.
#[repr(C)]
pub struct NativeDecoderHandle {
    _private: [u8; 0],
}

/// Status codes returned by decoder functions.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlStatus {
    /// Operation completed successfully.
    Success = 0,
    /// An error occurred. Call `jxl_get_last_error` for details.
    Error = 1,
    /// The decoder needs more input data.
    NeedMoreInput = 2,
    /// Invalid argument passed to function.
    InvalidArgument = 3,
    /// Buffer too small for output.
    BufferTooSmall = 4,
    /// Decoder is in an invalid state for this operation.
    InvalidState = 5,
}

/// Pixel data format.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlDataFormat {
    /// 8-bit unsigned integer per channel.
    Uint8 = 0,
    /// 16-bit unsigned integer per channel.
    Uint16 = 1,
    /// 16-bit float per channel.
    Float16 = 2,
    /// 32-bit float per channel.
    Float32 = 3,
}

/// Color channel layout.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlColorType {
    /// Red, green, blue, alpha (default for C# struct initialization).
    Rgba = 0,
    /// Single grayscale channel.
    Grayscale = 1,
    /// Grayscale + alpha.
    GrayscaleAlpha = 2,
    /// Red, green, blue.
    Rgb = 3,
    /// Blue, green, red (Windows bitmap order).
    Bgr = 4,
    /// Blue, green, red, alpha.
    Bgra = 5,
}

/// Endianness for multi-byte pixel formats.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlEndianness {
    /// Use native endianness of the platform.
    Native = 0,
    /// Little endian byte order.
    LittleEndian = 1,
    /// Big endian byte order.
    BigEndian = 2,
}

/// Pixel format specification.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlPixelFormat {
    /// Data format for each channel.
    pub DataFormat: JxlDataFormat,
    /// Color channel layout.
    pub ColorType: JxlColorType,
    /// Endianness for formats > 8 bits.
    pub Endianness: JxlEndianness,
}

/// Image orientation (EXIF-style).
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlOrientation {
    /// Normal orientation.
    Identity = 1,
    /// Flipped horizontally.
    FlipHorizontal = 2,
    /// Rotated 180 degrees.
    Rotate180 = 3,
    /// Flipped vertically.
    FlipVertical = 4,
    /// Transposed (swap x/y) then flipped horizontally.
    Transpose = 5,
    /// Rotated 90 degrees clockwise.
    Rotate90Cw = 6,
    /// Transposed then flipped vertically.
    AntiTranspose = 7,
    /// Rotated 90 degrees counter-clockwise.
    Rotate90Ccw = 8,
}

/// Progressive decoding mode.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlProgressiveMode {
    /// Renders all pixels in every call to Process.
    Eager = 0,
    /// Renders pixels once passes are completed.
    Pass = 1,
    /// Renders pixels only once the final frame is ready.
    FullFrame = 2,
}

/// Tone mapping parameters for HDR content.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
#[allow(non_snake_case)]
pub struct JxlToneMapping {
    /// Intensity target for HDR (nits).
    pub IntensityTarget: f32,
    /// Minimum nits for tone mapping.
    pub MinNits: f32,
    /// Linear tone mapping threshold (nits, or ratio if relative_to_max_display).
    pub LinearBelow: f32,
    /// Whether linear_below is relative to max display luminance.
    pub RelativeToMaxDisplay: bool,
}

/// Basic image information (raw FFI struct).
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
#[allow(non_snake_case)]
pub struct JxlBasicInfoRaw {
    /// Image width in pixels.
    pub Width: u32,
    /// Image height in pixels.
    pub Height: u32,
    /// Bits per sample for integer formats.
    pub BitsPerSample: u32,
    /// Exponent bits (0 for integer formats, >0 for float).
    pub ExponentBitsPerSample: u32,
    /// Number of color channels (1 for grayscale, 3 for RGB).
    pub NumColorChannels: u32,
    /// Number of extra channels (alpha, depth, etc.).
    pub NumExtraChannels: u32,
    /// Animation ticks per second numerator (0 if no animation).
    pub Animation_TpsNumerator: u32,
    /// Animation ticks per second denominator (0 if no animation).
    pub Animation_TpsDenominator: u32,
    /// Number of animation loops (0 = infinite).
    pub Animation_NumLoops: u32,
    /// Preview image width (0 if no preview).
    pub Preview_Width: u32,
    /// Preview image height (0 if no preview).
    pub Preview_Height: u32,
    /// Tone mapping parameters for HDR content.
    pub ToneMapping: JxlToneMapping,
    /// Image orientation.
    pub Orientation: JxlOrientation,
    /// Whether alpha is premultiplied.
    pub AlphaPremultiplied: bool,
    /// Whether the image is animated.
    pub IsAnimated: bool,
    /// Whether original color profile is used.
    pub UsesOriginalProfile: bool,
}

/// Extra channel type.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlExtraChannelType {
    /// Alpha/transparency channel.
    Alpha = 0,
    /// Depth map.
    Depth = 1,
    /// Spot color.
    SpotColor = 2,
    /// Selection mask.
    SelectionMask = 3,
    /// CFA (color filter array) for raw sensor data.
    Cfa = 4,
    /// Thermal data.
    Thermal = 5,
    /// Non-optional extra channel.
    NonOptional = 6,
    /// Optional extra channel.
    Optional = 7,
    /// Unknown channel type.
    Unknown = 255,
}

/// Information about an extra channel.
/// Note: jxl-rs API only exposes channel type and alpha_associated.
/// Other fields like bits_per_sample, name, spot_color are in the lower-level
/// ExtraChannelInfo but not exposed through the public API.
#[repr(C)]
#[derive(Debug, Clone)]
#[allow(non_snake_case)]
pub struct JxlExtraChannelInfo {
    /// Type of extra channel.
    pub ChannelType: JxlExtraChannelType,
    /// Whether alpha is associated/premultiplied (only for alpha channels).
    pub AlphaAssociated: bool,
}

/// Frame header information.
/// Note: jxl-rs API exposes name, duration, and size.
/// is_last is in the lower-level FrameHeader but not exposed through the API.
#[repr(C)]
#[derive(Debug, Clone)]
#[allow(non_snake_case)]
pub struct JxlFrameHeader {
    /// Frame duration in milliseconds (for animation).
    pub DurationMs: f32,
    /// Frame width in pixels.
    pub FrameWidth: u32,
    /// Frame height in pixels.
    pub FrameHeight: u32,
    /// Frame name length in bytes. Use jxl_decoder_get_frame_name to get the actual name.
    pub NameLength: u32,
}

impl Default for JxlBasicInfoRaw {
    fn default() -> Self {
        Self {
            Width: 0,
            Height: 0,
            BitsPerSample: 8,
            ExponentBitsPerSample: 0,
            NumColorChannels: 3,
            NumExtraChannels: 0,
            Animation_TpsNumerator: 0,
            Animation_TpsDenominator: 0,
            Animation_NumLoops: 0,
            Preview_Width: 0,
            Preview_Height: 0,
            ToneMapping: JxlToneMapping {
                IntensityTarget: 255.0,
                MinNits: 0.0,
                LinearBelow: 0.0,
                RelativeToMaxDisplay: false,
            },
            Orientation: JxlOrientation::Identity,
            AlphaPremultiplied: false,
            IsAnimated: false,
            UsesOriginalProfile: false,
        }
    }
}

impl Default for JxlPixelFormat {
    fn default() -> Self {
        Self {
            DataFormat: JxlDataFormat::Uint8,
            ColorType: JxlColorType::Rgba,
            Endianness: JxlEndianness::Native,
        }
    }
}

/// Decoder options.
/// All options should be set before decoding begins.
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
#[allow(non_snake_case)]
pub struct JxlDecodeOptions {
    /// Maximum number of pixels to decode.
    /// 0 = no limit.
    pub PixelLimit: usize,
    /// Desired intensity target for HDR content.
    /// 0 = use default (image's native intensity target).
    pub DesiredIntensityTarget: f32,
    /// Progressive decoding mode.
    pub ProgressiveMode: JxlProgressiveMode,
    /// Whether to adjust image orientation based on EXIF data.
    pub AdjustOrientation: bool,
    /// Whether to render spot colors.
    pub RenderSpotColors: bool,
    /// Whether to coalesce animation frames.
    pub Coalescing: bool,
    /// Whether to skip the preview image.
    pub SkipPreview: bool,
    /// Whether to enable output rendering.
    pub EnableOutput: bool,
    /// Whether to use high precision mode for decoding.
    pub HighPrecision: bool,
    /// Whether to premultiply alpha in the output.
    pub PremultiplyAlpha: bool,
    /// Whether to decode extra channels into separate buffers.
    pub DecodeExtraChannels: bool,
    /// Desired output pixel format.
    pub PixelFormat: JxlPixelFormat,
}

impl Default for JxlDecodeOptions {
    fn default() -> Self {
        Self {
            PixelLimit: 0,
            DesiredIntensityTarget: 0.0,
            ProgressiveMode: JxlProgressiveMode::Pass,
            AdjustOrientation: true,
            RenderSpotColors: true,
            Coalescing: true,
            SkipPreview: true,
            EnableOutput: true,
            HighPrecision: false,
            PremultiplyAlpha: false,
            DecodeExtraChannels: false,
            PixelFormat: JxlPixelFormat::default(),
        }
    }
}

/// Events returned by the streaming decoder's process function.
/// These indicate what stage the decoder has reached.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlDecoderEvent {
    /// An error occurred. Call `jxl_get_last_error` for details.
    Error = 0,
    /// The decoder needs more input data. Call `jxl_decoder_append_input`.
    NeedMoreInput = 1,
    /// Basic image info is now available. Call `jxl_decoder_get_basic_info`.
    HaveBasicInfo = 2,
    /// Frame header is available. Call `jxl_decoder_get_frame_header`.
    HaveFrameHeader = 3,
    /// Pixels are available. Call `jxl_decoder_read_pixels` with a buffer.
    NeedOutputBuffer = 4,
    /// A frame has been fully decoded.
    FrameComplete = 5,
    /// All frames have been decoded. The decoder is finished.
    Complete = 6,
}

/// Signature check result.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlSignature {
    /// Not enough data to determine.
    NotEnoughBytes = 0,
    /// Not a JPEG XL file.
    Invalid = 1,
    /// Valid JPEG XL codestream.
    Codestream = 2,
    /// Valid JPEG XL container.
    Container = 3,
}

// ============================================================================
// Color Profile Types
// ============================================================================

/// Opaque handle to a color profile.
/// Must be freed with `jxl_color_profile_free`.
#[repr(C)]
pub struct JxlColorProfileHandle {
    _private: [u8; 0],
}

/// Rendering intent for color management.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlRenderingIntent {
    /// Perceptual rendering intent.
    Perceptual = 0,
    /// Relative colorimetric rendering intent.
    Relative = 1,
    /// Saturation rendering intent.
    Saturation = 2,
    /// Absolute colorimetric rendering intent.
    Absolute = 3,
}

/// Tag for JxlWhitePointRaw discriminated union.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlWhitePointTag {
    /// D65 standard illuminant.
    D65 = 0,
    /// Equal energy illuminant.
    E = 1,
    /// DCI-P3 theater white point.
    Dci = 2,
    /// Custom chromaticity coordinates.
    Chromaticity = 3,
}

/// White point specification (tagged union).
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlWhitePointRaw {
    /// Discriminator tag.
    pub Tag: JxlWhitePointTag,
    /// X chromaticity coordinate (only valid when Tag == Chromaticity).
    pub Wx: f32,
    /// Y chromaticity coordinate (only valid when Tag == Chromaticity).
    pub Wy: f32,
}

/// Tag for JxlPrimariesRaw discriminated union.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlPrimariesTag {
    /// sRGB/Rec.709 primaries.
    Srgb = 0,
    /// BT.2100/Rec.2020 primaries.
    Bt2100 = 1,
    /// DCI-P3 primaries.
    P3 = 2,
    /// Custom chromaticity coordinates.
    Chromaticities = 3,
}

/// Color primaries specification (tagged union).
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlPrimariesRaw {
    /// Discriminator tag.
    pub Tag: JxlPrimariesTag,
    /// Red X chromaticity (only valid when Tag == Chromaticities).
    pub Rx: f32,
    /// Red Y chromaticity (only valid when Tag == Chromaticities).
    pub Ry: f32,
    /// Green X chromaticity (only valid when Tag == Chromaticities).
    pub Gx: f32,
    /// Green Y chromaticity (only valid when Tag == Chromaticities).
    pub Gy: f32,
    /// Blue X chromaticity (only valid when Tag == Chromaticities).
    pub Bx: f32,
    /// Blue Y chromaticity (only valid when Tag == Chromaticities).
    pub By: f32,
}

/// Tag for JxlTransferFunctionRaw discriminated union.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlTransferFunctionTag {
    /// BT.709 transfer function.
    Bt709 = 0,
    /// Linear (gamma 1.0).
    Linear = 1,
    /// sRGB transfer function.
    Srgb = 2,
    /// Perceptual Quantizer (HDR).
    Pq = 3,
    /// DCI gamma (~2.6).
    Dci = 4,
    /// Hybrid Log-Gamma (HDR).
    Hlg = 5,
    /// Custom gamma value.
    Gamma = 6,
}

/// Transfer function specification (tagged union).
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlTransferFunctionRaw {
    /// Discriminator tag.
    pub Tag: JxlTransferFunctionTag,
    /// Gamma value (only valid when Tag == Gamma).
    pub Gamma: f32,
}

/// Tag for JxlColorEncodingRaw discriminated union.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlColorEncodingTag {
    /// RGB color space.
    Rgb = 0,
    /// Grayscale color space.
    Grayscale = 1,
    /// XYB color space (JPEG XL internal).
    Xyb = 2,
}

/// Color encoding specification (tagged union).
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlColorEncodingRaw {
    /// Discriminator tag.
    pub Tag: JxlColorEncodingTag,
    /// White point (valid for Rgb and Grayscale).
    pub WhitePoint: JxlWhitePointRaw,
    /// Color primaries (only valid for Rgb).
    pub Primaries: JxlPrimariesRaw,
    /// Transfer function (valid for Rgb and Grayscale, not Xyb).
    pub TransferFunction: JxlTransferFunctionRaw,
    /// Rendering intent.
    pub RenderingIntent: JxlRenderingIntent,
}

/// Tag for JxlColorProfileRaw discriminated union.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlColorProfileTag {
    /// ICC profile (raw bytes).
    Icc = 0,
    /// Simple parameterized color encoding.
    Simple = 1,
}

/// Color profile specification (tagged union).
/// For ICC profiles, the data is returned separately via pointer/length.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
#[allow(non_snake_case)]
pub struct JxlColorProfileRaw {
    /// Discriminator tag.
    pub Tag: JxlColorProfileTag,
    /// ICC data length in bytes (only valid when Tag == Icc).
    pub IccLength: usize,
    /// Color encoding (only valid when Tag == Simple).
    pub Encoding: JxlColorEncodingRaw,
}

impl Default for JxlWhitePointRaw {
    fn default() -> Self {
        Self {
            Tag: JxlWhitePointTag::D65,
            Wx: 0.0,
            Wy: 0.0,
        }
    }
}

impl Default for JxlPrimariesRaw {
    fn default() -> Self {
        Self {
            Tag: JxlPrimariesTag::Srgb,
            Rx: 0.0,
            Ry: 0.0,
            Gx: 0.0,
            Gy: 0.0,
            Bx: 0.0,
            By: 0.0,
        }
    }
}

impl Default for JxlTransferFunctionRaw {
    fn default() -> Self {
        Self {
            Tag: JxlTransferFunctionTag::Srgb,
            Gamma: 0.0,
        }
    }
}

impl Default for JxlColorEncodingRaw {
    fn default() -> Self {
        Self {
            Tag: JxlColorEncodingTag::Rgb,
            WhitePoint: JxlWhitePointRaw::default(),
            Primaries: JxlPrimariesRaw::default(),
            TransferFunction: JxlTransferFunctionRaw::default(),
            RenderingIntent: JxlRenderingIntent::Perceptual,
        }
    }
}

impl Default for JxlColorProfileRaw {
    fn default() -> Self {
        Self {
            Tag: JxlColorProfileTag::Simple,
            IccLength: 0,
            Encoding: JxlColorEncodingRaw::default(),
        }
    }
}
