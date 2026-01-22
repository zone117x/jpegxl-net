// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Decoder implementation for the C API.

use crate::error::{clear_last_error, set_last_error};
use crate::types::*;
use jxl::api::{
    Endianness, JxlColorType, JxlDataFormat, JxlDecoder, JxlDecoderOptions, JxlPixelFormat,
    ProcessingResult,
};
use jxl::headers::extra_channels::ExtraChannel;
use jxl::headers::image_metadata::Orientation;
use jxl::image::JxlOutputBuffer;
use std::slice;

/// Internal decoder state machine.
enum DecoderState {
    /// Initial state, ready to accept input.
    Initialized(JxlDecoder<jxl::api::states::Initialized>),
    /// Has image info, can read pixels.
    WithImageInfo(JxlDecoder<jxl::api::states::WithImageInfo>),
    /// Has frame info, ready to decode pixels.
    WithFrameInfo(JxlDecoder<jxl::api::states::WithFrameInfo>),
    /// Transitional state during processing.
    Processing,
}

/// Internal decoder structure.
struct DecoderInner {
    /// Current decoder state.
    state: DecoderState,
    /// Raw JXL data (for one-shot decoding).
    data: Vec<u8>,
    /// Current read offset in data (tracks position between process calls).
    data_offset: usize,
    /// Cached basic info.
    basic_info: Option<JxlrsBasicInfo>,
    /// Cached extra channel info.
    extra_channels: Vec<JxlrsExtraChannelInfo>,
    /// Desired output pixel format.
    pixel_format: JxlrsPixelFormat,
}

impl DecoderInner {
    fn new() -> Self {
        let options = JxlDecoderOptions::default();
        Self {
            state: DecoderState::Initialized(JxlDecoder::new(options)),
            data: Vec::new(),
            data_offset: 0,
            basic_info: None,
            extra_channels: Vec::new(),
            pixel_format: JxlrsPixelFormat::default(),
        }
    }

    fn reset(&mut self) {
        let options = JxlDecoderOptions::default();
        self.state = DecoderState::Initialized(JxlDecoder::new(options));
        self.data.clear();
        self.data_offset = 0;
        self.basic_info = None;
        self.extra_channels.clear();
    }
}

// ============================================================================
// Decoder Lifecycle
// ============================================================================

/// Creates a new decoder instance.
///
/// # Returns
/// A pointer to the decoder, or null on allocation failure.
/// The decoder must be destroyed with `jxlrs_decoder_destroy`.
#[unsafe(no_mangle)]
pub extern "C" fn jxlrs_decoder_create() -> *mut JxlrsDecoder {
    clear_last_error();

    let decoder = Box::new(DecoderInner::new());
    Box::into_raw(decoder) as *mut JxlrsDecoder
}

/// Destroys a decoder instance and frees its resources.
///
/// # Safety
/// The decoder pointer must have been created by `jxlrs_decoder_create`.
/// After calling this function, the decoder pointer is invalid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_destroy(decoder: *mut JxlrsDecoder) {
    if !decoder.is_null() {
        unsafe {
            drop(Box::from_raw(decoder as *mut DecoderInner));
        }
    }
}

/// Resets the decoder to its initial state, allowing it to decode a new image.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_reset(decoder: *mut JxlrsDecoder) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    clear_last_error();
    inner.reset();

    JxlrsStatus::Success
}

// ============================================================================
// Input
// ============================================================================

/// Sets the input data for the decoder (one-shot decoding).
///
/// The decoder copies the data internally, so the caller can free
/// the input buffer after this call.
///
/// # Safety
/// - `decoder` must be a valid decoder pointer.
/// - `data` must point to `size` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_set_input(
    decoder: *mut JxlrsDecoder,
    data: *const u8,
    size: usize,
) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    if data.is_null() && size > 0 {
        set_last_error("Null data pointer with non-zero size");
        return JxlrsStatus::InvalidArgument;
    }

    clear_last_error();

    // Reset decoder state and store new data
    inner.reset();
    if size > 0 {
        inner
            .data
            .extend_from_slice(unsafe { slice::from_raw_parts(data, size) });
    }

    JxlrsStatus::Success
}

// ============================================================================
// Configuration
// ============================================================================

/// Sets the desired output pixel format.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_set_pixel_format(
    decoder: *mut JxlrsDecoder,
    format: *const JxlrsPixelFormat,
) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    let Some(format) = (unsafe { format.as_ref() }) else {
        set_last_error("Null format pointer");
        return JxlrsStatus::InvalidArgument;
    };

    clear_last_error();
    inner.pixel_format = *format;

    JxlrsStatus::Success
}

// ============================================================================
// Decoding - Basic Info
// ============================================================================

/// Decodes the image header and retrieves basic info.
///
/// This must be called before `jxlrs_decoder_get_pixels`.
///
/// # Safety
/// - `decoder` must be valid.
/// - `info` must point to a writable `JxlrsBasicInfo`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_read_info(
    decoder: *mut JxlrsDecoder,
    info: *mut JxlrsBasicInfo,
) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    if inner.data.is_empty() {
        set_last_error("No input data set");
        return JxlrsStatus::InvalidState;
    }

    clear_last_error();

    // If we already have cached info, return it
    if let Some(ref cached_info) = inner.basic_info {
        if let Some(out_info) = unsafe { info.as_mut() } {
            *out_info = cached_info.clone();
        }
        return JxlrsStatus::Success;
    }

    // Take ownership of the decoder state for processing
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    let decoder_initialized = match state {
        DecoderState::Initialized(d) => d,
        DecoderState::WithImageInfo(d) => {
            // Already processed, get info from this state
            let jxl_info = d.basic_info();
            let basic_info = convert_basic_info(jxl_info);
            inner.extra_channels = jxl_info
                .extra_channels
                .iter()
                .map(convert_extra_channel_info)
                .collect();
            inner.basic_info = Some(basic_info.clone());

            if let Some(out_info) = unsafe { info.as_mut() } {
                *out_info = basic_info;
            }

            inner.state = DecoderState::WithImageInfo(d);
            return JxlrsStatus::Success;
        }
        DecoderState::WithFrameInfo(d) => {
            // Already past image info stage
            inner.state = DecoderState::WithFrameInfo(d);
            if let Some(ref cached) = inner.basic_info {
                if let Some(out_info) = unsafe { info.as_mut() } {
                    *out_info = cached.clone();
                }
            }
            return JxlrsStatus::Success;
        }
        DecoderState::Processing => {
            set_last_error("Decoder is in an invalid state");
            return JxlrsStatus::InvalidState;
        }
    };

    // Process to get image info - use offset to continue from where we left off
    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    let result = decoder_initialized.process(&mut input_slice);
    inner.data_offset += len_before - input_slice.len();

    match result {
        Ok(ProcessingResult::Complete { result: decoder_with_info }) => {
            // Get and cache basic info
            let jxl_info = decoder_with_info.basic_info();
            let basic_info = convert_basic_info(jxl_info);
            inner.extra_channels = jxl_info
                .extra_channels
                .iter()
                .map(convert_extra_channel_info)
                .collect();
            inner.basic_info = Some(basic_info.clone());

            if let Some(out_info) = unsafe { info.as_mut() } {
                *out_info = basic_info;
            }

            inner.state = DecoderState::WithImageInfo(decoder_with_info);
            JxlrsStatus::Success
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::Initialized(fallback);
            set_last_error("Incomplete header data");
            JxlrsStatus::NeedMoreInput
        }
        Err(e) => {
            // Recreate decoder for next attempt
            inner.state = DecoderState::Initialized(JxlDecoder::new(JxlDecoderOptions::default()));
            set_last_error(format!("Failed to decode header: {}", e));
            JxlrsStatus::Error
        }
    }
}

/// Gets the number of extra channels.
///
/// Must be called after `jxlrs_decoder_read_info`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_get_extra_channel_count(
    decoder: *const JxlrsDecoder,
) -> u32 {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        return 0;
    };

    inner.extra_channels.len() as u32
}

/// Gets info about an extra channel.
///
/// # Safety
/// - `decoder` must be valid.
/// - `info` must point to a writable `JxlrsExtraChannelInfo`.
/// - `index` must be less than the extra channel count.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_get_extra_channel_info(
    decoder: *const JxlrsDecoder,
    index: u32,
    info: *mut JxlrsExtraChannelInfo,
) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    let Some(channel_info) = inner.extra_channels.get(index as usize) else {
        set_last_error(format!("Extra channel index {} out of range", index));
        return JxlrsStatus::InvalidArgument;
    };

    if let Some(out_info) = unsafe { info.as_mut() } {
        *out_info = channel_info.clone();
    }

    JxlrsStatus::Success
}

// ============================================================================
// Decoding - Pixels
// ============================================================================

/// Calculates the required buffer size for decoded pixels.
///
/// # Safety
/// `decoder` must be valid and `jxlrs_decoder_read_info` must have been called.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_get_buffer_size(decoder: *const JxlrsDecoder) -> usize {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        return 0;
    };

    let Some(ref info) = inner.basic_info else {
        return 0;
    };

    let bytes_per_sample = match inner.pixel_format.data_format {
        JxlrsDataFormat::Uint8 => 1,
        JxlrsDataFormat::Uint16 | JxlrsDataFormat::Float16 => 2,
        JxlrsDataFormat::Float32 => 4,
    };

    let samples_per_pixel = match inner.pixel_format.color_type {
        JxlrsColorType::Grayscale => 1,
        JxlrsColorType::GrayscaleAlpha => 2,
        JxlrsColorType::Rgb | JxlrsColorType::Bgr => 3,
        JxlrsColorType::Rgba | JxlrsColorType::Bgra => 4,
    };

    (info.width as usize) * (info.height as usize) * samples_per_pixel * bytes_per_sample
}

/// Decodes pixels into the provided buffer.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `buffer` - Output buffer for pixel data.
/// * `buffer_size` - Size of the buffer in bytes.
///
/// # Safety
/// - `decoder` must be valid.
/// - `buffer` must be valid for writes of `buffer_size` bytes.
/// - `jxlrs_decoder_read_info` must have been called first.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_decoder_get_pixels(
    decoder: *mut JxlrsDecoder,
    buffer: *mut u8,
    buffer_size: usize,
) -> JxlrsStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlrsStatus::InvalidArgument;
    };

    if buffer.is_null() {
        set_last_error("Null buffer pointer");
        return JxlrsStatus::InvalidArgument;
    }

    let Some(ref info) = inner.basic_info else {
        set_last_error("Must call jxlrs_decoder_read_info first");
        return JxlrsStatus::InvalidState;
    };

    let required_size = unsafe { jxlrs_decoder_get_buffer_size(decoder) };
    if buffer_size < required_size {
        set_last_error(format!(
            "Buffer too small: {} bytes provided, {} required",
            buffer_size, required_size
        ));
        return JxlrsStatus::BufferTooSmall;
    }

    clear_last_error();

    let width = info.width as usize;
    let height = info.height as usize;
    let num_extra = inner.extra_channels.len();

    // Calculate bytes per row
    let bytes_per_sample = match inner.pixel_format.data_format {
        JxlrsDataFormat::Uint8 => 1,
        JxlrsDataFormat::Uint16 | JxlrsDataFormat::Float16 => 2,
        JxlrsDataFormat::Float32 => 4,
    };

    let samples_per_pixel = match inner.pixel_format.color_type {
        JxlrsColorType::Grayscale => 1,
        JxlrsColorType::GrayscaleAlpha => 2,
        JxlrsColorType::Rgb | JxlrsColorType::Bgr => 3,
        JxlrsColorType::Rgba | JxlrsColorType::Bgra => 4,
    };

    let bytes_per_row = width * samples_per_pixel * bytes_per_sample;

    // Take ownership of decoder state
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    let mut decoder_with_info = match state {
        DecoderState::WithImageInfo(d) => d,
        DecoderState::WithFrameInfo(d) => {
            // Need to get pixels from frame info state
            let buffer_slice = unsafe { slice::from_raw_parts_mut(buffer, buffer_size) };
            let output_buffer = JxlOutputBuffer::new(buffer_slice, height, bytes_per_row);
            let mut buffers = [output_buffer];

            let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
            let len_before = input_slice.len();
            let result = d.process(&mut input_slice, &mut buffers);
            inner.data_offset += len_before - input_slice.len();
            
            match result {
                Ok(ProcessingResult::Complete { result }) => {
                    inner.state = DecoderState::WithImageInfo(result);
                    return JxlrsStatus::Success;
                }
                Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
                    inner.state = DecoderState::WithFrameInfo(fallback);
                    set_last_error("Incomplete pixel data");
                    return JxlrsStatus::NeedMoreInput;
                }
                Err(e) => {
                    inner.state =
                        DecoderState::Initialized(JxlDecoder::new(JxlDecoderOptions::default()));
                    set_last_error(format!("Pixel decode error: {}", e));
                    return JxlrsStatus::Error;
                }
            }
        }
        other => {
            inner.state = other;
            set_last_error("Must call jxlrs_decoder_read_info first");
            return JxlrsStatus::InvalidState;
        }
    };

    // Set pixel format - skip extra channels for simple one-shot decode
    // This means we only need one output buffer (for the main color data)
    let pixel_format = convert_to_jxl_pixel_format(&inner.pixel_format, num_extra, true);
    decoder_with_info.set_pixel_format(pixel_format);

    // Process to get frame info - continue from current offset
    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    let decoder_with_frame = match decoder_with_info.process(&mut input_slice) {
        Ok(ProcessingResult::Complete { result }) => {
            inner.data_offset += len_before - input_slice.len();
            result
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.data_offset += len_before - input_slice.len();
            inner.state = DecoderState::WithImageInfo(fallback);
            set_last_error("Incomplete frame data");
            return JxlrsStatus::NeedMoreInput;
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(JxlDecoder::new(JxlDecoderOptions::default()));
            set_last_error(format!("Frame decode error: {}", e));
            return JxlrsStatus::Error;
        }
    };

    // Now decode pixels - continue from current offset
    let buffer_slice = unsafe { slice::from_raw_parts_mut(buffer, buffer_size) };
    let output_buffer = JxlOutputBuffer::new(buffer_slice, height, bytes_per_row);
    let mut buffers = [output_buffer];

    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    let result = decoder_with_frame.process(&mut input_slice, &mut buffers);
    inner.data_offset += len_before - input_slice.len();
    
    match result {
        Ok(ProcessingResult::Complete { result }) => {
            inner.state = DecoderState::WithImageInfo(result);
            JxlrsStatus::Success
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            set_last_error("Incomplete pixel data");
            JxlrsStatus::NeedMoreInput
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(JxlDecoder::new(JxlDecoderOptions::default()));
            set_last_error(format!("Pixel decode error: {}", e));
            JxlrsStatus::Error
        }
    }
}

// ============================================================================
// Signature Check
// ============================================================================

/// Signature check result.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JxlrsSignature {
    /// Not enough data to determine.
    NotEnoughBytes = 0,
    /// Not a JPEG XL file.
    Invalid = 1,
    /// Valid JPEG XL codestream.
    Codestream = 2,
    /// Valid JPEG XL container.
    Container = 3,
}

/// Checks if data appears to be a JPEG XL file.
///
/// Only needs the first 12 bytes to determine.
///
/// # Safety
/// `data` must be valid for reads of `size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxlrs_signature_check(data: *const u8, size: usize) -> JxlrsSignature {
    if data.is_null() || size < 2 {
        return JxlrsSignature::NotEnoughBytes;
    }

    let bytes = unsafe { slice::from_raw_parts(data, size.min(12)) };

    // Check for codestream signature (0xFF 0x0A)
    if bytes.len() >= 2 && bytes[0] == 0xFF && bytes[1] == 0x0A {
        return JxlrsSignature::Codestream;
    }

    // Check for container signature
    if bytes.len() >= 12 {
        let container_sig: [u8; 12] = [
            0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A,
        ];
        if bytes == container_sig {
            return JxlrsSignature::Container;
        }
    }

    if size < 12 {
        JxlrsSignature::NotEnoughBytes
    } else {
        JxlrsSignature::Invalid
    }
}

// ============================================================================
// Helper Functions
// ============================================================================

fn convert_basic_info(info: &jxl::api::JxlBasicInfo) -> JxlrsBasicInfo {
    let (anim_num, anim_den, anim_loops) = info
        .animation
        .as_ref()
        .map_or((0, 0, 0), |a| (a.tps_numerator, a.tps_denominator, a.num_loops));

    let (preview_w, preview_h) = info.preview_size.unwrap_or((0, 0));

    // Determine bits_per_sample and exponent_bits
    let (bits, exp_bits) = match &info.bit_depth {
        jxl::api::JxlBitDepth::Int { bits_per_sample } => (*bits_per_sample, 0),
        jxl::api::JxlBitDepth::Float {
            bits_per_sample,
            exponent_bits_per_sample,
        } => (*bits_per_sample, *exponent_bits_per_sample),
    };

    JxlrsBasicInfo {
        width: info.size.0 as u32,
        height: info.size.1 as u32,
        bits_per_sample: bits,
        exponent_bits_per_sample: exp_bits,
        num_color_channels: 3, // RGB, grayscale handled by color_type
        num_extra_channels: info.extra_channels.len() as u32,
        alpha_premultiplied: 0, // TODO: Check actual value from extra channels
        orientation: convert_orientation(info.orientation),
        have_animation: if info.animation.is_some() { 1 } else { 0 },
        animation_tps_numerator: anim_num,
        animation_tps_denominator: anim_den,
        animation_num_loops: anim_loops,
        uses_original_profile: if info.uses_original_profile { 1 } else { 0 },
        preview_width: preview_w as u32,
        preview_height: preview_h as u32,
        intensity_target: info.tone_mapping.intensity_target,
        min_nits: info.tone_mapping.min_nits,
    }
}

fn convert_orientation(orientation: Orientation) -> JxlrsOrientation {
    match orientation {
        Orientation::Identity => JxlrsOrientation::Identity,
        Orientation::FlipHorizontal => JxlrsOrientation::FlipHorizontal,
        Orientation::Rotate180 => JxlrsOrientation::Rotate180,
        Orientation::FlipVertical => JxlrsOrientation::FlipVertical,
        Orientation::Transpose => JxlrsOrientation::Transpose,
        Orientation::Rotate90Cw => JxlrsOrientation::Rotate90Cw,
        Orientation::AntiTranspose => JxlrsOrientation::AntiTranspose,
        Orientation::Rotate90Ccw => JxlrsOrientation::Rotate90Ccw,
    }
}

fn convert_extra_channel_info(channel: &jxl::api::JxlExtraChannel) -> JxlrsExtraChannelInfo {
    let channel_type = match channel.ec_type {
        ExtraChannel::Alpha => JxlrsExtraChannelType::Alpha,
        ExtraChannel::Depth => JxlrsExtraChannelType::Depth,
        ExtraChannel::SpotColor => JxlrsExtraChannelType::SpotColor,
        ExtraChannel::SelectionMask => JxlrsExtraChannelType::SelectionMask,
        ExtraChannel::CFA => JxlrsExtraChannelType::Cfa,
        ExtraChannel::Thermal => JxlrsExtraChannelType::Thermal,
        ExtraChannel::Optional => JxlrsExtraChannelType::Optional,
        _ => JxlrsExtraChannelType::Unknown,
    };

    JxlrsExtraChannelInfo {
        channel_type,
        bits_per_sample: 8, // Default, actual value may need to be retrieved differently
        exponent_bits_per_sample: 0,
        alpha_premultiplied: if channel.alpha_associated { 1 } else { 0 },
        spot_color: [0.0; 4],
        name_length: 0,
    }
}

fn convert_to_jxl_pixel_format(format: &JxlrsPixelFormat, num_extra_channels: usize, skip_extra_channels: bool) -> JxlPixelFormat {
    let color_type = match format.color_type {
        JxlrsColorType::Grayscale => JxlColorType::Grayscale,
        JxlrsColorType::GrayscaleAlpha => JxlColorType::GrayscaleAlpha,
        JxlrsColorType::Rgb => JxlColorType::Rgb,
        JxlrsColorType::Rgba => JxlColorType::Rgba,
        JxlrsColorType::Bgr => JxlColorType::Bgr,
        JxlrsColorType::Bgra => JxlColorType::Bgra,
    };

    let endianness = match format.endianness {
        JxlrsEndianness::Native => Endianness::native(),
        JxlrsEndianness::LittleEndian => Endianness::LittleEndian,
        JxlrsEndianness::BigEndian => Endianness::BigEndian,
    };

    let data_format = match format.data_format {
        JxlrsDataFormat::Uint8 => Some(JxlDataFormat::U8 { bit_depth: 8 }),
        JxlrsDataFormat::Uint16 => Some(JxlDataFormat::U16 {
            endianness,
            bit_depth: 16,
        }),
        JxlrsDataFormat::Float16 => Some(JxlDataFormat::F16 { endianness }),
        JxlrsDataFormat::Float32 => Some(JxlDataFormat::F32 { endianness }),
    };

    // If skipping extra channels, set them all to None so they won't be decoded
    let extra_channel_format = if skip_extra_channels {
        vec![None; num_extra_channels]
    } else {
        let extra_format = match format.data_format {
            JxlrsDataFormat::Uint8 => Some(JxlDataFormat::U8 { bit_depth: 8 }),
            JxlrsDataFormat::Uint16 => Some(JxlDataFormat::U16 {
                endianness,
                bit_depth: 16,
            }),
            JxlrsDataFormat::Float16 => Some(JxlDataFormat::F16 { endianness }),
            JxlrsDataFormat::Float32 => Some(JxlDataFormat::F32 { endianness }),
        };
        vec![extra_format; num_extra_channels]
    };

    JxlPixelFormat {
        color_type,
        color_data_format: data_format,
        extra_channel_format,
    }
}
