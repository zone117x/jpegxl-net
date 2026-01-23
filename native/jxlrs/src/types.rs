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
pub struct JxlPixelFormat {
    /// Data format for each channel.
    pub data_format: JxlDataFormat,
    /// Color channel layout.
    pub color_type: JxlColorType,
    /// Endianness for formats > 8 bits.
    pub endianness: JxlEndianness,
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

/// Basic image information.
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
pub struct JxlBasicInfo {
    /// Image width in pixels.
    pub width: u32,
    /// Image height in pixels.
    pub height: u32,
    /// Bits per sample for integer formats.
    pub bits_per_sample: u32,
    /// Exponent bits (0 for integer formats, >0 for float).
    pub exponent_bits_per_sample: u32,
    /// Number of color channels (1 for grayscale, 3 for RGB).
    pub num_color_channels: u32,
    /// Number of extra channels (alpha, depth, etc.).
    pub num_extra_channels: u32,
    /// Animation ticks per second numerator (0 if no animation).
    pub animation_tps_numerator: u32,
    /// Animation ticks per second denominator (0 if no animation).
    pub animation_tps_denominator: u32,
    /// Number of animation loops (0 = infinite).
    pub animation_num_loops: u32,
    /// Preview image width (0 if no preview).
    pub preview_width: u32,
    /// Preview image height (0 if no preview).
    pub preview_height: u32,
    /// Intensity target for HDR (nits).
    pub intensity_target: f32,
    /// Minimum nits for tone mapping.
    pub min_nits: f32,
    /// Image orientation.
    pub orientation: JxlOrientation,
    /// Whether alpha is premultiplied.
    pub alpha_premultiplied: bool,
    /// Whether the image has animation.
    pub have_animation: bool,
    /// Whether original color profile is used.
    pub uses_original_profile: bool,
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
pub struct JxlExtraChannelInfo {
    /// Spot color values (RGBA, only for spot color channels).
    pub spot_color: [f32; 4],
    /// Bits per sample.
    pub bits_per_sample: u32,
    /// Exponent bits (for float channels).
    pub exponent_bits_per_sample: u32,
    /// Channel name length in bytes (excluding null terminator).
    pub name_length: u32,
    /// Type of extra channel.
    pub channel_type: JxlExtraChannelType,
    /// Whether alpha is premultiplied (only for alpha channels).
    pub alpha_premultiplied: bool,
}

/// Frame header information.
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
pub struct JxlFrameHeader {
    /// Frame duration in milliseconds (for animation).
    pub duration_ms: f32,
    /// Frame width in pixels.
    pub frame_width: u32,
    /// Frame height in pixels.
    pub frame_height: u32,
    /// Frame name length in bytes (excluding null terminator).
    pub name_length: u32,
    /// Whether this is the last frame.
    pub is_last: bool,
}

impl Default for JxlBasicInfo {
    fn default() -> Self {
        Self {
            width: 0,
            height: 0,
            bits_per_sample: 8,
            exponent_bits_per_sample: 0,
            num_color_channels: 3,
            num_extra_channels: 0,
            animation_tps_numerator: 0,
            animation_tps_denominator: 0,
            animation_num_loops: 0,
            preview_width: 0,
            preview_height: 0,
            intensity_target: 255.0,
            min_nits: 0.0,
            orientation: JxlOrientation::Identity,
            alpha_premultiplied: false,
            have_animation: false,
            uses_original_profile: false,
        }
    }
}

impl Default for JxlPixelFormat {
    fn default() -> Self {
        Self {
            data_format: JxlDataFormat::Uint8,
            color_type: JxlColorType::Rgba,
            endianness: JxlEndianness::Native,
        }
    }
}

/// Decoder options (C-compatible struct).
/// All options should be set before decoding begins.
/// Fields are ordered by size (largest first) to minimize padding.
#[repr(C)]
#[derive(Debug, Clone)]
pub struct JxlDecoderOptionsC {
    /// Maximum number of pixels to decode.
    /// 0 = no limit.
    pub pixel_limit: usize,
    /// Desired intensity target for HDR content.
    /// 0 = use default (image's native intensity target).
    pub desired_intensity_target: f32,
    /// Progressive decoding mode.
    pub progressive_mode: JxlProgressiveMode,
    /// Whether to adjust image orientation based on EXIF data.
    pub adjust_orientation: bool,
    /// Whether to render spot colors.
    pub render_spot_colors: bool,
    /// Whether to coalesce animation frames.
    pub coalescing: bool,
    /// Whether to skip the preview image.
    pub skip_preview: bool,
    /// Whether to enable output rendering.
    pub enable_output: bool,
    /// Whether to use high precision mode for decoding.
    pub high_precision: bool,
    /// Whether to premultiply alpha in the output.
    pub premultiply_alpha: bool,
    /// Whether to decode extra channels into separate buffers.
    pub decode_extra_channels: bool,
}

impl Default for JxlDecoderOptionsC {
    fn default() -> Self {
        Self {
            pixel_limit: 0,
            desired_intensity_target: 0.0,
            progressive_mode: JxlProgressiveMode::Pass,
            adjust_orientation: true,
            render_spot_colors: true,
            coalescing: true,
            skip_preview: true,
            enable_output: true,
            high_precision: false,
            premultiply_alpha: false,
            decode_extra_channels: false,
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
