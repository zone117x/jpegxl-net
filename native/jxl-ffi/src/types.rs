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
    /// Single grayscale channel.
    Grayscale = 0,
    /// Grayscale + alpha.
    GrayscaleAlpha = 1,
    /// Red, green, blue.
    Rgb = 2,
    /// Red, green, blue, alpha.
    Rgba = 3,
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
    pub AnimationTpsNumerator: u32,
    /// Animation ticks per second denominator (0 if no animation).
    pub AnimationTpsDenominator: u32,
    /// Number of animation loops (0 = infinite).
    pub AnimationNumLoops: u32,
    /// Preview image width (0 if no preview).
    pub PreviewWidth: u32,
    /// Preview image height (0 if no preview).
    pub PreviewHeight: u32,
    /// Intensity target for HDR (nits).
    pub IntensityTarget: f32,
    /// Minimum nits for tone mapping.
    pub MinNits: f32,
    /// Whether linear_below is relative to max display luminance.
    pub RelativeToMaxDisplay: bool,
    /// Linear tone mapping threshold (nits, or ratio if relative_to_max_display).
    pub LinearBelow: f32,
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
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
#[allow(non_snake_case)]
pub struct JxlExtraChannelInfo {
    /// Spot color values (RGBA, only for spot color channels).
    pub SpotColor: [f32; 4],
    /// Bits per sample.
    pub BitsPerSample: u32,
    /// Exponent bits (for float channels).
    pub ExponentBitsPerSample: u32,
    /// Channel name length in bytes (excluding null terminator).
    pub NameLength: u32,
    /// Type of extra channel.
    pub ChannelType: JxlExtraChannelType,
    /// Whether alpha is associated/premultiplied (only for alpha channels).
    pub AlphaAssociated: bool,
}

/// Frame header information.
/// Fields are ordered by size (largest first) to minimize padding.
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
    /// Whether this is the last frame.
    pub IsLast: bool,
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
            AnimationTpsNumerator: 0,
            AnimationTpsDenominator: 0,
            AnimationNumLoops: 0,
            PreviewWidth: 0,
            PreviewHeight: 0,
            IntensityTarget: 255.0,
            MinNits: 0.0,
            RelativeToMaxDisplay: false,
            LinearBelow: 0.0,
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
