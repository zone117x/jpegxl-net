// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Decoder implementation for the C API.

use crate::conversions::{
    bytes_per_sample, calculate_buffer_size, calculate_bytes_per_row, convert_basic_info,
    convert_extra_channel_info, convert_frame_header, convert_options_to_upstream,
    convert_to_jxl_pixel_format,
};
use crate::error::{clear_last_error, set_last_error};
use crate::types::*;
use jxl::api::ProcessingResult;
use jxl::image::JxlOutputBuffer;
use std::slice;

// Type alias for upstream decoder
type UpstreamDecoder<S> = jxl::api::JxlDecoder<S>;

// ============================================================================
// Decoder Pointer Validation Macros
// ============================================================================

/// Gets a mutable reference to the decoder, returning an error if null.
macro_rules! get_decoder_mut {
    ($decoder:expr, $error_return:expr) => {
        match unsafe { ($decoder as *mut DecoderInner).as_mut() } {
            Some(inner) => inner,
            None => {
                set_last_error("Null decoder pointer");
                return $error_return;
            }
        }
    };
}

/// Gets an immutable reference to the decoder, returning an error if null.
macro_rules! get_decoder_ref {
    ($decoder:expr, $error_return:expr) => {
        match unsafe { ($decoder as *const DecoderInner).as_ref() } {
            Some(inner) => inner,
            None => {
                set_last_error("Null decoder pointer");
                return $error_return;
            }
        }
    };
}

/// Gets an immutable reference to the decoder, returning silently if null.
macro_rules! get_decoder_ref_silent {
    ($decoder:expr, $error_return:expr) => {
        match unsafe { ($decoder as *const DecoderInner).as_ref() } {
            Some(inner) => inner,
            None => return $error_return,
        }
    };
}

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
    /// Cached basic info (needed for WithFrameInfo state which doesn't expose it).
    basic_info: Option<JxlBasicInfoRaw>,
    /// Cached extra channel info (needed for pixel format conversion).
    extra_channels: Vec<JxlExtraChannelInfo>,
    /// Desired output pixel format.
    pixel_format: JxlPixelFormat,
    /// Decoder options (stored for reset).
    options: JxlDecodeOptions,
}

impl DecoderInner {
    fn new() -> Self {
        Self::with_options(JxlDecodeOptions::default())
    }

    fn with_options(options: JxlDecodeOptions) -> Self {
        Self {
            state: DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&options))),
            data: Vec::new(),
            data_offset: 0,
            basic_info: None,
            extra_channels: Vec::new(),
            pixel_format: options.PixelFormat,
            options,
        }
    }

    fn reset(&mut self) {
        self.reset_state();
        self.data.clear();
        self.data_offset = 0;
        self.basic_info = None;
        self.extra_channels.clear();
    }

    /// Rewinds the decoder to the beginning of the input without clearing the data buffer.
    /// This allows re-decoding the same input without calling SetInput again.
    fn rewind(&mut self) {
        self.reset_state();
        self.data_offset = 0;
        self.basic_info = None;
        self.extra_channels.clear();
    }

    /// Resets only the decoder state (used for error recovery).
    fn reset_state(&mut self) {
        self.state = DecoderState::Initialized(UpstreamDecoder::new(convert_options_to_upstream(&self.options)));
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
/// If `options` is not null, it must point to a valid `JxlDecodeOptions` struct.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_create_with_options(
    options: *const JxlDecodeOptions,
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
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    clear_last_error();
    inner.reset();

    JxlStatus::Success
}

/// Rewinds the decoder to the beginning of the input without clearing the data buffer.
/// This allows re-decoding the same input without calling SetInput again.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_rewind(decoder: *mut NativeDecoderHandle) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    clear_last_error();
    inner.rewind();

    JxlStatus::Success
}

// ============================================================================
// Input
// ============================================================================

/// Appends input data to the decoder's buffer.
///
/// The decoder copies the data internally, so the caller can free
/// the input buffer after this call. Does not reset decoder state,
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
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

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
    let inner = get_decoder_mut!(decoder, JxlDecoderEvent::Error);

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
                    inner.reset_state();
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
            // Skip extra channels unless DecodeExtraChannels is enabled
            let skip_extra = !inner.options.DecodeExtraChannels;
            let pixel_format = convert_to_jxl_pixel_format(&inner.pixel_format, &inner.extra_channels, skip_extra);
            decoder_with_info.set_pixel_format(pixel_format);

            // Try to get frame info
            let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
            let len_before = input_slice.len();
            let result = decoder_with_info.process(&mut input_slice);
            inner.data_offset += len_before - input_slice.len();

            match result {
                Ok(ProcessingResult::Complete { result: decoder_with_frame }) => {
                    inner.state = DecoderState::WithFrameInfo(decoder_with_frame);
                    JxlDecoderEvent::HaveFrameHeader
                }
                Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
                    inner.state = DecoderState::WithImageInfo(fallback);
                    JxlDecoderEvent::NeedMoreInput
                }
                Err(e) => {
                    inner.reset_state();
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
/// - `info` must point to a writable `JxlBasicInfoRaw`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_basic_info(
    decoder: *const NativeDecoderHandle,
    info: *mut JxlBasicInfoRaw,
) -> JxlStatus {
    let inner = get_decoder_ref!(decoder, JxlStatus::InvalidArgument);

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
    let inner = get_decoder_ref!(decoder, JxlStatus::InvalidArgument);

    let DecoderState::WithFrameInfo(ref decoder_with_frame) = inner.state else {
        set_last_error("Frame header not yet available - call jxl_decoder_process until HaveFrameHeader");
        return JxlStatus::InvalidState;
    };

    if let Some(out_header) = unsafe { header.as_mut() } {
        let jxl_header = decoder_with_frame.frame_header();
        // is_last is not known until frame decode completes
        *out_header = convert_frame_header(&jxl_header, false);
    }

    JxlStatus::Success
}

/// Gets the current frame's name.
///
/// Only valid after `jxl_decoder_process` returns `HaveFrameHeader`.
/// Returns the number of bytes written to buffer, or the required size if buffer is null/too small.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `buffer` - Output buffer for the UTF-8 name, or null to query required size.
/// * `buffer_size` - Size of the buffer in bytes.
///
/// # Returns
/// The number of bytes written, or the required buffer size if buffer is null or too small.
/// Returns 0 if no frame header is available or the frame has no name.
///
/// # Safety
/// - `decoder` must be valid.
/// - If `buffer` is not null, it must be valid for writes of `buffer_size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_frame_name(
    decoder: *const NativeDecoderHandle,
    buffer: *mut u8,
    buffer_size: u32,
) -> u32 {
    let inner = get_decoder_ref_silent!(decoder, 0);

    let DecoderState::WithFrameInfo(ref decoder_with_frame) = inner.state else {
        return 0;
    };

    let header = decoder_with_frame.frame_header();
    let name_bytes = header.name.as_bytes();
    let name_len = name_bytes.len() as u32;

    // If no name, return 0
    if name_len == 0 {
        return 0;
    }

    // If no buffer or buffer too small, return required size
    if buffer.is_null() || buffer_size < name_len {
        return name_len;
    }

    // Copy name to buffer
    unsafe {
        std::ptr::copy_nonoverlapping(name_bytes.as_ptr(), buffer, name_len as usize);
    }

    name_len
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
    let inner = get_decoder_mut!(decoder, JxlDecoderEvent::Error);

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

    let height = info.Height as usize;
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
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.reset_state();
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
    let inner = get_decoder_ref_silent!(decoder, false);

    match &inner.state {
        DecoderState::WithImageInfo(d) => d.has_more_frames(),
        DecoderState::WithFrameInfo(_) => true, // We have a frame, so there's at least one more
        _ => false,
    }
}

/// Skips the current frame without decoding pixels.
///
/// Call this after `jxl_decoder_process` returns `NeedOutputBuffer` when you
/// only need frame metadata (duration, name, etc.) and don't need the pixels.
/// This is much faster than `jxl_decoder_read_pixels` as it doesn't decode
/// pixel data.
///
/// After successful completion, call `jxl_decoder_process` again to
/// get `FrameComplete` or continue with the next frame.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_skip_frame(
    decoder: *mut NativeDecoderHandle,
) -> JxlDecoderEvent {
    let inner = get_decoder_mut!(decoder, JxlDecoderEvent::Error);

    clear_last_error();

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

    // Skip frame without decoding pixels
    let mut input_slice: &[u8] = &inner.data[inner.data_offset..];
    let len_before = input_slice.len();
    let result = decoder_with_frame.skip_frame(&mut input_slice);
    inner.data_offset += len_before - input_slice.len();

    match result {
        Ok(ProcessingResult::Complete { result }) => {
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.reset_state();
            set_last_error(format!("Skip frame error: {}", e));
            JxlDecoderEvent::Error
        }
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
    let inner = get_decoder_ref_silent!(decoder, 0);

    let Some(ref info) = inner.basic_info else {
        return 0;
    };

    if index as usize >= inner.extra_channels.len() {
        return 0;
    }

    // Extra channels are single-plane, so calculate based on width * height * bytes_per_sample
    let width = info.Width as usize;
    let height = info.Height as usize;
    let bytes_per_sample = bytes_per_sample(inner.pixel_format.DataFormat);
    
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
    let inner = get_decoder_mut!(decoder, JxlDecoderEvent::Error);

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

    let height = info.Height as usize;
    let width = info.Width as usize;
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
    let extra_bytes_per_sample = bytes_per_sample(inner.pixel_format.DataFormat);
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
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.reset_state();
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
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

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
    let inner = get_decoder_ref_silent!(decoder, 0);

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
    let inner = get_decoder_ref!(decoder, JxlStatus::InvalidArgument);

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
    let inner = get_decoder_ref_silent!(decoder, 0);

    let Some(ref info) = inner.basic_info else {
        return 0;
    };

    calculate_buffer_size(info, &inner.pixel_format)
}

// ============================================================================
// Signature Check
// ============================================================================

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

#[cfg(test)]
#[path = "decoder_tests.rs"]
mod tests;