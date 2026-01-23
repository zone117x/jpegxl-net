// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Decoder implementation for the C API.

use crate::error::{clear_last_error, set_last_error};
use crate::types::*;
// Use fully qualified paths for upstream jxl types to avoid conflicts with our types
use jxl::api::{Endianness, JxlDecoderOptions, ProcessingResult};
use jxl::api::JxlProgressiveMode as UpstreamProgressiveMode;
use jxl::headers::extra_channels::ExtraChannel;
use jxl::headers::image_metadata::Orientation;
use jxl::image::JxlOutputBuffer;
use std::slice;

// Type aliases for upstream jxl types to distinguish from our C API types
type UpstreamDecoder<S> = jxl::api::JxlDecoder<S>;
type UpstreamPixelFormat = jxl::api::JxlPixelFormat;
type UpstreamColorType = jxl::api::JxlColorType;
type UpstreamDataFormat = jxl::api::JxlDataFormat;

/// Internal decoder state machine.
enum DecoderState {
    /// Initial state, ready to accept input.
    Initialized(UpstreamDecoder<jxl::api::states::Initialized>),
    /// Has image info, can read pixels.
    WithImageInfo(UpstreamDecoder<jxl::api::states::WithImageInfo>),
    /// Has frame info, ready to decode pixels.
    WithFrameInfo(UpstreamDecoder<jxl::api::states::WithFrameInfo>),
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
    basic_info: Option<JxlBasicInfo>,
    /// Cached frame header.
    frame_header: Option<JxlFrameHeader>,
    /// Cached extra channel info.
    extra_channels: Vec<JxlExtraChannelInfo>,
    /// Desired output pixel format.
    pixel_format: JxlPixelFormat,
    /// Decoder options (stored for reset).
    options: JxlDecoderOptionsC,
}

/// Converts C-compatible options to upstream decoder options.
fn convert_options_to_upstream(c_options: &JxlDecoderOptionsC) -> JxlDecoderOptions {
    let mut options = JxlDecoderOptions::default();
    options.adjust_orientation = c_options.adjust_orientation;
    options.render_spot_colors = c_options.render_spot_colors;
    options.coalescing = c_options.coalescing;
    options.desired_intensity_target = if c_options.desired_intensity_target > 0.0 {
        Some(c_options.desired_intensity_target)
    } else {
        None
    };
    options.skip_preview = c_options.skip_preview;
    options.progressive_mode = match c_options.progressive_mode {
        JxlProgressiveMode::Eager => UpstreamProgressiveMode::Eager,
        JxlProgressiveMode::Pass => UpstreamProgressiveMode::Pass,
        JxlProgressiveMode::FullFrame => UpstreamProgressiveMode::FullFrame,
    };
    options.enable_output = c_options.enable_output;
    options.pixel_limit = if c_options.pixel_limit > 0 {
        Some(c_options.pixel_limit)
    } else {
        None
    };
    options.high_precision = c_options.high_precision;
    options.premultiply_output = c_options.premultiply_alpha;
    options
}

impl DecoderInner {
    fn new() -> Self {
        Self::with_options(JxlDecoderOptionsC::default())
    }

    fn with_options(options: JxlDecoderOptionsC) -> Self {
        Self {
            state: DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&options))),
            data: Vec::new(),
            data_offset: 0,
            basic_info: None,
            frame_header: None,
            extra_channels: Vec::new(),
            pixel_format: JxlPixelFormat::default(),
            options,
        }
    }

    fn reset(&mut self) {
        self.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&self.options)));
        self.data.clear();
        self.data_offset = 0;
        self.basic_info = None;
        self.frame_header = None;
        self.extra_channels.clear();
    }
}

// ============================================================================
// Decoder Lifecycle
// ============================================================================

/// Creates a new decoder instance with default options.
///
/// # Returns
/// A pointer to the decoder, or null on allocation failure.
/// The decoder must be destroyed with `jxl_decoder_destroy`.
#[unsafe(no_mangle)]
pub extern "C" fn jxl_decoder_create() -> *mut NativeDecoderHandle {
    clear_last_error();

    let decoder = Box::new(DecoderInner::new());
    Box::into_raw(decoder) as *mut NativeDecoderHandle
}

/// Creates a new decoder instance with the specified options.
///
/// This is the preferred way to create a decoder with custom options.
/// Options are immutable after creation for efficiency.
///
/// # Arguments
/// * `options` - Pointer to decoder options, or null to use defaults.
///
/// # Returns
/// A pointer to the decoder, or null on allocation failure.
/// The decoder must be destroyed with `jxl_decoder_destroy`.
///
/// # Safety
/// If `options` is not null, it must point to a valid `JxlDecoderOptionsC` struct.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_create_with_options(
    options: *const JxlDecoderOptionsC,
) -> *mut NativeDecoderHandle {
    clear_last_error();

    let decoder = if options.is_null() {
        Box::new(DecoderInner::new())
    } else {
        Box::new(DecoderInner::with_options(unsafe { (*options).clone() }))
    };

    Box::into_raw(decoder) as *mut NativeDecoderHandle
}

/// Destroys a decoder instance and frees its resources.
///
/// # Safety
/// The decoder pointer must have been created by `jxl_decoder_create`.
/// After calling this function, the decoder pointer is invalid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_destroy(decoder: *mut NativeDecoderHandle) {
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
pub unsafe extern "C" fn jxl_decoder_reset(decoder: *mut NativeDecoderHandle) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    clear_last_error();
    inner.reset();

    JxlStatus::Success
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
pub unsafe extern "C" fn jxl_decoder_set_input(
    decoder: *mut NativeDecoderHandle,
    data: *const u8,
    size: usize,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    if data.is_null() && size > 0 {
        set_last_error("Null data pointer with non-zero size");
        return JxlStatus::InvalidArgument;
    }

    clear_last_error();

    // Reset decoder state and store new data
    inner.reset();
    if size > 0 {
        inner
            .data
            .extend_from_slice(unsafe { slice::from_raw_parts(data, size) });
    }

    JxlStatus::Success
}

/// Appends more input data to the decoder's buffer (streaming decoding).
///
/// Unlike `jxl_decoder_set_input`, this does not reset the decoder state,
/// allowing incremental feeding of data.
///
/// # Safety
/// - `decoder` must be a valid decoder pointer.
/// - `data` must point to `size` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_append_input(
    decoder: *mut NativeDecoderHandle,
    data: *const u8,
    size: usize,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    if data.is_null() && size > 0 {
        set_last_error("Null data pointer with non-zero size");
        return JxlStatus::InvalidArgument;
    }

    clear_last_error();

    // Append data without resetting
    if size > 0 {
        inner
            .data
            .extend_from_slice(unsafe { slice::from_raw_parts(data, size) });
    }

    JxlStatus::Success
}

/// Processes the current input data and returns the next decoder event.
///
/// This is the main function for streaming decoding. Call it repeatedly,
/// handling each event appropriately:
/// - `NeedMoreInput`: Call `jxl_decoder_append_input` with more data
/// - `HaveBasicInfo`: Image info is available, call `jxl_decoder_get_basic_info`
/// - `HaveFrameHeader`: Frame header is available, call `jxl_decoder_get_frame_header`
/// - `NeedOutputBuffer`: Ready to decode pixels, call `jxl_decoder_read_pixels`
/// - `FrameComplete`: Frame is done, check for more frames or call again
/// - `Complete`: All frames decoded, decoding is finished
/// - `Error`: Check `jxl_get_last_error` for details
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_process(
    decoder: *mut NativeDecoderHandle,
) -> JxlDecoderEvent {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlDecoderEvent::Error;
    };

    clear_last_error();

    // Take ownership of the decoder state for processing
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    match state {
        DecoderState::Initialized(decoder_init) => {
            // Try to get image info
            let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
            let len_before = input_slice.len();
            let result = decoder_init.process(&mut input_slice);
            inner.data_offset += len_before - input_slice.len();

            match result {
                Ok(ProcessingResult::Complete { result: decoder_with_info }) => {
                    // Cache basic info
                    let jxl_info = decoder_with_info.basic_info();
                    let basic_info = convert_basic_info(jxl_info);
                    inner.extra_channels = jxl_info
                        .extra_channels
                        .iter()
                        .map(convert_extra_channel_info)
                        .collect();
                    inner.basic_info = Some(basic_info);
                    inner.state = DecoderState::WithImageInfo(decoder_with_info);
                    JxlDecoderEvent::HaveBasicInfo
                }
                Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
                    inner.state = DecoderState::Initialized(fallback);
                    JxlDecoderEvent::NeedMoreInput
                }
                Err(e) => {
                    inner.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&inner.options)));
                    set_last_error(format!("Failed to decode header: {}", e));
                    JxlDecoderEvent::Error
                }
            }
        }
        DecoderState::WithImageInfo(mut decoder_with_info) => {
            // Check if there are more frames
            if !decoder_with_info.has_more_frames() {
                inner.state = DecoderState::WithImageInfo(decoder_with_info);
                return JxlDecoderEvent::Complete;
            }

            // Set pixel format before processing frame
            // Skip extra channels unless decode_extra_channels is enabled
            let skip_extra = !inner.options.decode_extra_channels;
            let pixel_format = convert_to_jxl_pixel_format(&inner.pixel_format, &inner.extra_channels, skip_extra);
            decoder_with_info.set_pixel_format(pixel_format);

            // Try to get frame info
            let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
            let len_before = input_slice.len();
            let result = decoder_with_info.process(&mut input_slice);
            inner.data_offset += len_before - input_slice.len();

            match result {
                Ok(ProcessingResult::Complete { result: decoder_with_frame }) => {
                    // Cache the frame header
                    let frame_header = decoder_with_frame.frame_header();
                    inner.frame_header = Some(convert_frame_header(&frame_header));
                    inner.state = DecoderState::WithFrameInfo(decoder_with_frame);
                    JxlDecoderEvent::HaveFrameHeader
                }
                Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
                    inner.state = DecoderState::WithImageInfo(fallback);
                    JxlDecoderEvent::NeedMoreInput
                }
                Err(e) => {
                    inner.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&inner.options)));
                    set_last_error(format!("Failed to decode frame header: {}", e));
                    JxlDecoderEvent::Error
                }
            }
        }
        DecoderState::WithFrameInfo(decoder_with_frame) => {
            // Signal that we need an output buffer to decode pixels
            inner.state = DecoderState::WithFrameInfo(decoder_with_frame);
            JxlDecoderEvent::NeedOutputBuffer
        }
        DecoderState::Processing => {
            set_last_error("Decoder is in an invalid state");
            JxlDecoderEvent::Error
        }
    }
}

/// Gets the basic image info (streaming API).
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Safety
/// - `decoder` must be valid.
/// - `info` must point to a writable `JxlBasicInfo`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_basic_info(
    decoder: *const NativeDecoderHandle,
    info: *mut JxlBasicInfo,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    let Some(ref cached_info) = inner.basic_info else {
        set_last_error("Basic info not yet available - call jxl_decoder_process first");
        return JxlStatus::InvalidState;
    };

    if let Some(out_info) = unsafe { info.as_mut() } {
        *out_info = cached_info.clone();
    }

    JxlStatus::Success
}

/// Gets the current frame header (streaming API).
///
/// Only valid after `jxl_decoder_process` returns `HaveFrameHeader`.
///
/// # Safety
/// - `decoder` must be valid.
/// - `header` must point to a writable `JxlFrameHeader`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_frame_header(
    decoder: *const NativeDecoderHandle,
    header: *mut JxlFrameHeader,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    let Some(ref cached_header) = inner.frame_header else {
        set_last_error("Frame header not yet available - call jxl_decoder_process until HaveFrameHeader");
        return JxlStatus::InvalidState;
    };

    if let Some(out_header) = unsafe { header.as_mut() } {
        *out_header = cached_header.clone();
    }

    JxlStatus::Success
}

/// Decodes pixels into the provided buffer (streaming API).
///
/// Call this after `jxl_decoder_process` returns `NeedOutputBuffer`.
/// After successful completion, call `jxl_decoder_process` again to
/// get `FrameComplete` or continue with the next frame.
///
/// # Safety
/// - `decoder` must be valid.
/// - `buffer` must be valid for writes of `buffer_size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_read_pixels(
    decoder: *mut NativeDecoderHandle,
    buffer: *mut u8,
    buffer_size: usize,
) -> JxlDecoderEvent {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlDecoderEvent::Error;
    };

    if buffer.is_null() {
        set_last_error("Null buffer pointer");
        return JxlDecoderEvent::Error;
    }

    let Some(ref info) = inner.basic_info else {
        set_last_error("Basic info not available");
        return JxlDecoderEvent::Error;
    };

    let required_size = calculate_buffer_size(info, &inner.pixel_format);
    if buffer_size < required_size {
        set_last_error(format!(
            "Buffer too small: {} bytes provided, {} required",
            buffer_size, required_size
        ));
        return JxlDecoderEvent::Error;
    }

    clear_last_error();

    let height = info.height as usize;
    let bytes_per_row = calculate_bytes_per_row(info, &inner.pixel_format);

    // Take ownership of decoder state
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    let decoder_with_frame = match state {
        DecoderState::WithFrameInfo(d) => d,
        other => {
            inner.state = other;
            set_last_error("Must call jxl_decoder_process until NeedOutputBuffer first");
            return JxlDecoderEvent::Error;
        }
    };

    // Decode pixels
    let buffer_slice = unsafe { slice::from_raw_parts_mut(buffer, buffer_size) };
    let output_buffer = JxlOutputBuffer::new(buffer_slice, height, bytes_per_row);
    let mut buffers = [output_buffer];

    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    let result = decoder_with_frame.process(&mut input_slice, &mut buffers);
    inner.data_offset += len_before - input_slice.len();

    match result {
        Ok(ProcessingResult::Complete { result }) => {
            // Update is_last in cached frame header based on whether there are more frames
            if let Some(ref mut header) = inner.frame_header {
                header.is_last = !result.has_more_frames();
            }
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&inner.options)));
            set_last_error(format!("Pixel decode error: {}", e));
            JxlDecoderEvent::Error
        }
    }
}

/// Checks if the decoder has more frames to decode.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_has_more_frames(
    decoder: *const NativeDecoderHandle,
) -> bool {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        return false;
    };

    match &inner.state {
        DecoderState::WithImageInfo(d) => d.has_more_frames(),
        DecoderState::WithFrameInfo(_) => true, // We have a frame, so there's at least one more
        _ => false,
    }
}

// ============================================================================
// Extra Channels
// ============================================================================

/// Calculates the required buffer size for a specific extra channel.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `index` - The extra channel index (0-based).
///
/// # Returns
/// The required buffer size in bytes, or 0 if invalid.
///
/// # Safety
/// `decoder` must be valid and basic info must be available (after `HaveBasicInfo` event).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_extra_channel_buffer_size(
    decoder: *const NativeDecoderHandle,
    index: u32,
) -> usize {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        return 0;
    };

    let Some(ref info) = inner.basic_info else {
        return 0;
    };

    if index as usize >= inner.extra_channels.len() {
        return 0;
    }

    // Extra channels are single-plane, so calculate based on width * height * bytes_per_sample
    let width = info.width as usize;
    let height = info.height as usize;
    let bytes_per_sample = bytes_per_sample(inner.pixel_format.data_format);
    
    width * height * bytes_per_sample
}

/// Decodes pixels with extra channels into separate buffers.
///
/// The first buffer receives color data (RGB/RGBA/etc.), subsequent buffers
/// receive extra channels in order. Set buffer to null to skip that channel.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `color_buffer` - Output buffer for color data.
/// * `color_buffer_size` - Size of color buffer in bytes.
/// * `extra_buffers` - Array of pointers to extra channel buffers (can contain nulls to skip).
/// * `extra_buffer_sizes` - Array of buffer sizes for each extra channel.
/// * `num_extra_buffers` - Number of extra buffers provided.
///
/// # Safety
/// - `decoder` must be valid.
/// - `color_buffer` must be valid for writes of `color_buffer_size` bytes.
/// - `extra_buffers` must point to `num_extra_buffers` pointers.
/// - Each non-null buffer must be valid for writes of its corresponding size.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_read_pixels_with_extra_channels(
    decoder: *mut NativeDecoderHandle,
    color_buffer: *mut u8,
    color_buffer_size: usize,
    extra_buffers: *const *mut u8,
    extra_buffer_sizes: *const usize,
    num_extra_buffers: usize,
) -> JxlDecoderEvent {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlDecoderEvent::Error;
    };

    if color_buffer.is_null() {
        set_last_error("Null color buffer pointer");
        return JxlDecoderEvent::Error;
    }

    let Some(ref info) = inner.basic_info else {
        set_last_error("Basic info not available");
        return JxlDecoderEvent::Error;
    };

    let required_color_size = calculate_buffer_size(info, &inner.pixel_format);
    if color_buffer_size < required_color_size {
        set_last_error(format!(
            "Color buffer too small: {} bytes provided, {} required",
            color_buffer_size, required_color_size
        ));
        return JxlDecoderEvent::Error;
    }

    clear_last_error();

    let height = info.height as usize;
    let width = info.width as usize;
    let color_bytes_per_row = calculate_bytes_per_row(info, &inner.pixel_format);
    let num_extra = inner.extra_channels.len();

    // Take ownership of decoder state
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    let decoder_with_frame = match state {
        DecoderState::WithFrameInfo(d) => d,
        other => {
            inner.state = other;
            set_last_error("Must call jxl_decoder_process until NeedOutputBuffer first");
            return JxlDecoderEvent::Error;
        }
    };

    // Build output buffers - one for color, one for each extra channel
    let color_slice = unsafe { slice::from_raw_parts_mut(color_buffer, color_buffer_size) };
    let color_output = JxlOutputBuffer::new(color_slice, height, color_bytes_per_row);
    
    // Build extra channel buffers
    let extra_bytes_per_sample = bytes_per_sample(inner.pixel_format.data_format);
    let extra_bytes_per_row = width * extra_bytes_per_sample;
    
    let extra_buffer_ptrs = if !extra_buffers.is_null() && num_extra_buffers > 0 {
        unsafe { slice::from_raw_parts(extra_buffers, num_extra_buffers) }
    } else {
        &[]
    };
    
    let extra_sizes = if !extra_buffer_sizes.is_null() && num_extra_buffers > 0 {
        unsafe { slice::from_raw_parts(extra_buffer_sizes, num_extra_buffers) }
    } else {
        &[]
    };
    
    // Create a vector of output buffers - color first, then extras
    // Note: We need to handle the case where not all extra channels have buffers
    let mut all_buffers: Vec<JxlOutputBuffer> = Vec::with_capacity(1 + num_extra.min(num_extra_buffers));
    all_buffers.push(color_output);
    
    for i in 0..num_extra.min(num_extra_buffers) {
        let ptr = extra_buffer_ptrs.get(i).copied().unwrap_or(std::ptr::null_mut());
        let size = extra_sizes.get(i).copied().unwrap_or(0);
        
        if !ptr.is_null() && size >= height * extra_bytes_per_row {
            let slice = unsafe { slice::from_raw_parts_mut(ptr, size) };
            all_buffers.push(JxlOutputBuffer::new(slice, height, extra_bytes_per_row));
        }
    }

    // Note: The pixel format (including extra channel format) was already set when
    // jxl_decoder_process transitioned to WithFrameInfo. The decode_extra_channels
    // flag must be set before that transition.

    // Decode pixels
    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    
    // We need to use a mutable borrow of all_buffers
    let result = decoder_with_frame.process(&mut input_slice, &mut all_buffers);
    inner.data_offset += len_before - input_slice.len();

    match result {
        Ok(ProcessingResult::Complete { result }) => {
            if let Some(ref mut header) = inner.frame_header {
                header.is_last = !result.has_more_frames();
            }
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&inner.options)));
            set_last_error(format!("Pixel decode error: {}", e));
            JxlDecoderEvent::Error
        }
    }
}

// ============================================================================
// Configuration
// ============================================================================

/// Sets the desired output pixel format.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_set_pixel_format(
    decoder: *mut NativeDecoderHandle,
    format: *const JxlPixelFormat,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    let Some(format) = (unsafe { format.as_ref() }) else {
        set_last_error("Null format pointer");
        return JxlStatus::InvalidArgument;
    };

    clear_last_error();
    inner.pixel_format = *format;

    JxlStatus::Success
}


/// Gets the number of extra channels.
///
/// Must be called after basic info is available (after `HaveBasicInfo` event).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_extra_channel_count(
    decoder: *const NativeDecoderHandle,
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
/// - `info` must point to a writable `JxlExtraChannelInfo`.
/// - `index` must be less than the extra channel count.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_extra_channel_info(
    decoder: *const NativeDecoderHandle,
    index: u32,
    info: *mut JxlExtraChannelInfo,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    let Some(channel_info) = inner.extra_channels.get(index as usize) else {
        set_last_error(format!("Extra channel index {} out of range", index));
        return JxlStatus::InvalidArgument;
    };

    if let Some(out_info) = unsafe { info.as_mut() } {
        *out_info = channel_info.clone();
    }

    JxlStatus::Success
}

// ============================================================================
// Decoding - Pixels
// ============================================================================

/// Calculates the required buffer size for decoded pixels.
///
/// # Safety
/// `decoder` must be valid and basic info must be available (after `HaveBasicInfo` event).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_buffer_size(decoder: *const NativeDecoderHandle) -> usize {
    let Some(inner) = (unsafe { (decoder as *const DecoderInner).as_ref() }) else {
        return 0;
    };

    let Some(ref info) = inner.basic_info else {
        return 0;
    };

    calculate_buffer_size(info, &inner.pixel_format)
}

// ============================================================================
// Signature Check
// ============================================================================

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

/// Checks if data appears to be a JPEG XL file.
///
/// Only needs the first 12 bytes to determine.
///
/// # Safety
/// `data` must be valid for reads of `size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_signature_check(data: *const u8, size: usize) -> JxlSignature {
    if data.is_null() || size == 0 {
        return JxlSignature::NotEnoughBytes;
    }

    let bytes = unsafe { slice::from_raw_parts(data, size) };
    
    match jxl::api::check_signature(bytes) {
        ProcessingResult::Complete { result: Some(sig_type) } => {
            match sig_type {
                jxl::api::JxlSignatureType::Codestream => JxlSignature::Codestream,
                jxl::api::JxlSignatureType::Container => JxlSignature::Container,
            }
        }
        ProcessingResult::Complete { result: None } => JxlSignature::Invalid,
        ProcessingResult::NeedsMoreInput { .. } => JxlSignature::NotEnoughBytes,
    }
}

// ============================================================================
// Helper Functions
// ============================================================================

/// Calculates bytes per sample based on data format.
fn bytes_per_sample(data_format: JxlDataFormat) -> usize {
    match data_format {
        JxlDataFormat::Uint8 => 1,
        JxlDataFormat::Uint16 | JxlDataFormat::Float16 => 2,
        JxlDataFormat::Float32 => 4,
    }
}

/// Calculates samples per pixel based on color type.
fn samples_per_pixel(color_type: JxlColorType) -> usize {
    match color_type {
        JxlColorType::Grayscale => 1,
        JxlColorType::GrayscaleAlpha => 2,
        JxlColorType::Rgb | JxlColorType::Bgr => 3,
        JxlColorType::Rgba | JxlColorType::Bgra => 4,
    }
}

/// Calculates the bytes per row for the given image info and pixel format.
fn calculate_bytes_per_row(info: &JxlBasicInfo, pixel_format: &JxlPixelFormat) -> usize {
    let width = info.width as usize;
    let bps = bytes_per_sample(pixel_format.data_format);
    let spp = samples_per_pixel(pixel_format.color_type);
    width * spp * bps
}

/// Calculates the required buffer size for the given image info and pixel format.
fn calculate_buffer_size(info: &JxlBasicInfo, pixel_format: &JxlPixelFormat) -> usize {
    let height = info.height as usize;
    calculate_bytes_per_row(info, pixel_format) * height
}

fn convert_basic_info(info: &jxl::api::JxlBasicInfo) -> JxlBasicInfo {
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

    JxlBasicInfo {
        width: info.size.0 as u32,
        height: info.size.1 as u32,
        bits_per_sample: bits,
        exponent_bits_per_sample: exp_bits,
        num_color_channels: 3, // RGB, grayscale handled by color_type
        num_extra_channels: info.extra_channels.len() as u32,
        animation_tps_numerator: anim_num,
        animation_tps_denominator: anim_den,
        animation_num_loops: anim_loops,
        preview_width: preview_w as u32,
        preview_height: preview_h as u32,
        intensity_target: info.tone_mapping.intensity_target,
        min_nits: info.tone_mapping.min_nits,
        orientation: convert_orientation(info.orientation),
        alpha_premultiplied: false, // TODO: Check actual value from extra channels
        have_animation: info.animation.is_some(),
        uses_original_profile: info.uses_original_profile,
    }
}

fn convert_orientation(orientation: Orientation) -> JxlOrientation {
    match orientation {
        Orientation::Identity => JxlOrientation::Identity,
        Orientation::FlipHorizontal => JxlOrientation::FlipHorizontal,
        Orientation::Rotate180 => JxlOrientation::Rotate180,
        Orientation::FlipVertical => JxlOrientation::FlipVertical,
        Orientation::Transpose => JxlOrientation::Transpose,
        Orientation::Rotate90Cw => JxlOrientation::Rotate90Cw,
        Orientation::AntiTranspose => JxlOrientation::AntiTranspose,
        Orientation::Rotate90Ccw => JxlOrientation::Rotate90Ccw,
    }
}

fn convert_frame_header(header: &jxl::api::JxlFrameHeader) -> JxlFrameHeader {
    JxlFrameHeader {
        duration_ms: header.duration.unwrap_or(0.0) as f32,
        frame_width: header.size.0 as u32,
        frame_height: header.size.1 as u32,
        name_length: header.name.len() as u32,
        is_last: false, // Will be updated when we know if there are more frames
    }
}

fn convert_extra_channel_info(channel: &jxl::api::JxlExtraChannel) -> JxlExtraChannelInfo {
    let channel_type = match channel.ec_type {
        ExtraChannel::Alpha => JxlExtraChannelType::Alpha,
        ExtraChannel::Depth => JxlExtraChannelType::Depth,
        ExtraChannel::SpotColor => JxlExtraChannelType::SpotColor,
        ExtraChannel::SelectionMask => JxlExtraChannelType::SelectionMask,
        ExtraChannel::CFA => JxlExtraChannelType::Cfa,
        ExtraChannel::Thermal => JxlExtraChannelType::Thermal,
        ExtraChannel::Optional => JxlExtraChannelType::Optional,
        _ => JxlExtraChannelType::Unknown,
    };

    JxlExtraChannelInfo {
        spot_color: [0.0; 4],
        bits_per_sample: 8, // Default, actual value may need to be retrieved differently
        exponent_bits_per_sample: 0,
        name_length: 0,
        channel_type,
        alpha_premultiplied: channel.alpha_associated,
    }
}

fn convert_to_jxl_pixel_format(
    format: &JxlPixelFormat, 
    extra_channels: &[JxlExtraChannelInfo], 
    skip_extra_channels: bool
) -> UpstreamPixelFormat {
    let color_type = match format.color_type {
        JxlColorType::Grayscale => UpstreamColorType::Grayscale,
        JxlColorType::GrayscaleAlpha => UpstreamColorType::GrayscaleAlpha,
        JxlColorType::Rgb => UpstreamColorType::Rgb,
        JxlColorType::Rgba => UpstreamColorType::Rgba,
        JxlColorType::Bgr => UpstreamColorType::Bgr,
        JxlColorType::Bgra => UpstreamColorType::Bgra,
    };

    let endianness = match format.endianness {
        JxlEndianness::Native => Endianness::native(),
        JxlEndianness::LittleEndian => Endianness::LittleEndian,
        JxlEndianness::BigEndian => Endianness::BigEndian,
    };

    let data_format = match format.data_format {
        JxlDataFormat::Uint8 => Some(UpstreamDataFormat::U8 { bit_depth: 8 }),
        JxlDataFormat::Uint16 => Some(UpstreamDataFormat::U16 {
            endianness,
            bit_depth: 16,
        }),
        JxlDataFormat::Float16 => Some(UpstreamDataFormat::F16 { endianness }),
        JxlDataFormat::Float32 => Some(UpstreamDataFormat::F32 { endianness }),
    };

    // Determine if the color type already includes alpha
    let color_includes_alpha = matches!(
        format.color_type,
        JxlColorType::Rgba | JxlColorType::Bgra | JxlColorType::GrayscaleAlpha
    );

    // If skipping extra channels, set them all to None so they won't be decoded
    let extra_channel_format = if skip_extra_channels {
        vec![None; extra_channels.len()]
    } else {
        let extra_format = match format.data_format {
            JxlDataFormat::Uint8 => Some(UpstreamDataFormat::U8 { bit_depth: 8 }),
            JxlDataFormat::Uint16 => Some(UpstreamDataFormat::U16 {
                endianness,
                bit_depth: 16,
            }),
            JxlDataFormat::Float16 => Some(UpstreamDataFormat::F16 { endianness }),
            JxlDataFormat::Float32 => Some(UpstreamDataFormat::F32 { endianness }),
        };
        
        // Track whether we've skipped the first alpha channel (when color includes alpha)
        let mut first_alpha_skipped = false;
        
        extra_channels.iter().map(|ec| {
            // If color type includes alpha and this is the first alpha channel, skip it
            // (it's already part of the color output)
            if color_includes_alpha && ec.channel_type == JxlExtraChannelType::Alpha && !first_alpha_skipped {
                first_alpha_skipped = true;
                None
            } else {
                extra_format
            }
        }).collect()
    };

    UpstreamPixelFormat {
        color_type,
        color_data_format: data_format,
        extra_channel_format,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_convert_to_jxl_pixel_format_rgba_with_alpha() {
        let format = JxlPixelFormat {
            color_type: JxlColorType::Rgba,
            data_format: JxlDataFormat::Uint8,
            endianness: JxlEndianness::Native,
        };
        
        let extra_channels = vec![JxlExtraChannelInfo {
            channel_type: JxlExtraChannelType::Alpha,
            bits_per_sample: 8,
            exponent_bits_per_sample: 0,
            name_length: 0,
            spot_color: [0.0; 4],
            alpha_premultiplied: false,
        }];
        
        // When using RGBA with alpha as extra channel, alpha should be None
        // (alpha is already in the RGBA color output)
        let pixel_format = convert_to_jxl_pixel_format(&format, &extra_channels, false);
        
        assert_eq!(pixel_format.extra_channel_format.len(), 1);
        assert!(pixel_format.extra_channel_format[0].is_none(), 
            "Alpha should be None when using RGBA");
    }
    
    #[test]
    fn test_convert_to_jxl_pixel_format_rgb_with_alpha() {
        let format = JxlPixelFormat {
            color_type: JxlColorType::Rgb,
            data_format: JxlDataFormat::Uint8,
            endianness: JxlEndianness::Native,
        };
        
        let extra_channels = vec![JxlExtraChannelInfo {
            channel_type: JxlExtraChannelType::Alpha,
            bits_per_sample: 8,
            exponent_bits_per_sample: 0,
            name_length: 0,
            spot_color: [0.0; 4],
            alpha_premultiplied: false,
        }];
        
        // When using RGB (no alpha in color), alpha should be Some
        // (alpha needs to go to a separate buffer)
        let pixel_format = convert_to_jxl_pixel_format(&format, &extra_channels, false);
        
        assert_eq!(pixel_format.extra_channel_format.len(), 1);
        assert!(pixel_format.extra_channel_format[0].is_some(), 
            "Alpha should be Some when using RGB");
    }
}