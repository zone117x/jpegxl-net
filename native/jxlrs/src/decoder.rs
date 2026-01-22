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
    /// Cached extra channel info.
    extra_channels: Vec<JxlExtraChannelInfo>,
    /// Desired output pixel format.
    pixel_format: JxlPixelFormat,
    /// Decoder options (stored for reset).
    options: DecoderOptions,
}

/// Decoder options mirroring JxlDecoderOptions from upstream.
#[derive(Clone)]
struct DecoderOptions {
    adjust_orientation: bool,
    render_spot_colors: bool,
    coalescing: bool,
    desired_intensity_target: Option<f32>,
    skip_preview: bool,
    progressive_mode: JxlProgressiveMode,
    enable_output: bool,
    pixel_limit: Option<usize>,
    high_precision: bool,
    premultiply_output: bool,
}

impl Default for DecoderOptions {
    fn default() -> Self {
        Self {
            adjust_orientation: true,
            render_spot_colors: true,
            coalescing: true,
            desired_intensity_target: None,
            skip_preview: true,
            progressive_mode: JxlProgressiveMode::Pass,
            enable_output: true,
            pixel_limit: None,
            high_precision: false,
            premultiply_output: false,
        }
    }
}

impl DecoderOptions {
    fn to_upstream(&self) -> JxlDecoderOptions {
        let mut options = JxlDecoderOptions::default();
        options.adjust_orientation = self.adjust_orientation;
        options.render_spot_colors = self.render_spot_colors;
        options.coalescing = self.coalescing;
        options.desired_intensity_target = self.desired_intensity_target;
        options.skip_preview = self.skip_preview;
        options.progressive_mode = match self.progressive_mode {
            JxlProgressiveMode::Eager => UpstreamProgressiveMode::Eager,
            JxlProgressiveMode::Pass => UpstreamProgressiveMode::Pass,
            JxlProgressiveMode::FullFrame => UpstreamProgressiveMode::FullFrame,
        };
        options.enable_output = self.enable_output;
        options.pixel_limit = self.pixel_limit;
        options.high_precision = self.high_precision;
        options.premultiply_output = self.premultiply_output;
        options
    }

    fn from_c(c_options: &JxlDecoderOptionsC) -> Self {
        Self {
            adjust_orientation: c_options.adjust_orientation,
            render_spot_colors: c_options.render_spot_colors,
            coalescing: c_options.coalescing,
            desired_intensity_target: if c_options.desired_intensity_target > 0.0 {
                Some(c_options.desired_intensity_target)
            } else {
                None
            },
            skip_preview: c_options.skip_preview,
            progressive_mode: c_options.progressive_mode,
            enable_output: c_options.enable_output,
            pixel_limit: if c_options.pixel_limit > 0 {
                Some(c_options.pixel_limit)
            } else {
                None
            },
            high_precision: c_options.high_precision,
            premultiply_output: c_options.premultiply_alpha,
        }
    }
}

impl DecoderInner {
    fn new() -> Self {
        Self::with_options(DecoderOptions::default())
    }

    fn with_options(options: DecoderOptions) -> Self {
        Self {
            state: DecoderState::Initialized(UpstreamDecoder::new(options.to_upstream())),
            data: Vec::new(),
            data_offset: 0,
            basic_info: None,
            extra_channels: Vec::new(),
            pixel_format: JxlPixelFormat::default(),
            options,
        }
    }

    fn reset(&mut self) {
        self.state = DecoderState::Initialized(UpstreamDecoder::new(self.options.to_upstream()));
        self.data.clear();
        self.data_offset = 0;
        self.basic_info = None;
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

    let decoder_options = if options.is_null() {
        DecoderOptions::default()
    } else {
        DecoderOptions::from_c(unsafe { &*options })
    };

    let decoder = Box::new(DecoderInner::with_options(decoder_options));
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
/// - `NeedOutputBuffer`: Ready to decode pixels, call `jxl_decoder_get_pixels`
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
                    inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
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
            let num_extra = inner.extra_channels.len();
            let pixel_format = convert_to_jxl_pixel_format(&inner.pixel_format, num_extra, true);
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
                    inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
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
            inner.state = DecoderState::WithImageInfo(result);
            JxlDecoderEvent::FrameComplete
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            JxlDecoderEvent::NeedMoreInput
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
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

// ============================================================================
// Decoding - Basic Info
// ============================================================================

/// Decodes the image header and retrieves basic info.
///
/// This must be called before `jxl_decoder_get_pixels`.
///
/// # Safety
/// - `decoder` must be valid.
/// - `info` must point to a writable `JxlBasicInfo`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_read_info(
    decoder: *mut NativeDecoderHandle,
    info: *mut JxlBasicInfo,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    if inner.data.is_empty() {
        set_last_error("No input data set");
        return JxlStatus::InvalidState;
    }

    clear_last_error();

    // If we already have cached info, return it
    if let Some(ref cached_info) = inner.basic_info {
        if let Some(out_info) = unsafe { info.as_mut() } {
            *out_info = cached_info.clone();
        }
        return JxlStatus::Success;
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
            return JxlStatus::Success;
        }
        DecoderState::WithFrameInfo(d) => {
            // Already past image info stage
            inner.state = DecoderState::WithFrameInfo(d);
            if let Some(ref cached) = inner.basic_info {
                if let Some(out_info) = unsafe { info.as_mut() } {
                    *out_info = cached.clone();
                }
            }
            return JxlStatus::Success;
        }
        DecoderState::Processing => {
            set_last_error("Decoder is in an invalid state");
            return JxlStatus::InvalidState;
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
            JxlStatus::Success
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::Initialized(fallback);
            set_last_error("Incomplete header data");
            JxlStatus::NeedMoreInput
        }
        Err(e) => {
            // Recreate decoder for next attempt
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
            set_last_error(format!("Failed to decode header: {}", e));
            JxlStatus::Error
        }
    }
}

/// Gets the number of extra channels.
///
/// Must be called after `jxl_decoder_read_info`.
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
/// `decoder` must be valid and `jxl_decoder_read_info` must have been called.
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
/// - `jxl_decoder_read_info` must have been called first.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_pixels(
    decoder: *mut NativeDecoderHandle,
    buffer: *mut u8,
    buffer_size: usize,
) -> JxlStatus {
    let Some(inner) = (unsafe { (decoder as *mut DecoderInner).as_mut() }) else {
        set_last_error("Null decoder pointer");
        return JxlStatus::InvalidArgument;
    };

    if buffer.is_null() {
        set_last_error("Null buffer pointer");
        return JxlStatus::InvalidArgument;
    }

    let Some(ref info) = inner.basic_info else {
        set_last_error("Must call jxl_decoder_read_info first");
        return JxlStatus::InvalidState;
    };

    let required_size = unsafe { jxl_decoder_get_buffer_size(decoder) };
    if buffer_size < required_size {
        set_last_error(format!(
            "Buffer too small: {} bytes provided, {} required",
            buffer_size, required_size
        ));
        return JxlStatus::BufferTooSmall;
    }

    clear_last_error();

    let height = info.height as usize;
    let num_extra = inner.extra_channels.len();
    let bytes_per_row = calculate_bytes_per_row(info, &inner.pixel_format);

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
                    return JxlStatus::Success;
                }
                Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
                    inner.state = DecoderState::WithFrameInfo(fallback);
                    set_last_error("Incomplete pixel data");
                    return JxlStatus::NeedMoreInput;
                }
                Err(e) => {
                    inner.state =
                        DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
                    set_last_error(format!("Pixel decode error: {}", e));
                    return JxlStatus::Error;
                }
            }
        }
        other => {
            inner.state = other;
            set_last_error("Must call jxl_decoder_read_info first");
            return JxlStatus::InvalidState;
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
            return JxlStatus::NeedMoreInput;
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
            set_last_error(format!("Frame decode error: {}", e));
            return JxlStatus::Error;
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
            JxlStatus::Success
        }
        Ok(ProcessingResult::NeedsMoreInput { fallback, .. }) => {
            inner.state = DecoderState::WithFrameInfo(fallback);
            set_last_error("Incomplete pixel data");
            JxlStatus::NeedMoreInput
        }
        Err(e) => {
            inner.state = DecoderState::Initialized(UpstreamDecoder::new(inner.options.to_upstream()));
            set_last_error(format!("Pixel decode error: {}", e));
            JxlStatus::Error
        }
    }
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
    if data.is_null() || size < 2 {
        return JxlSignature::NotEnoughBytes;
    }

    let bytes = unsafe { slice::from_raw_parts(data, size.min(12)) };

    // Check for codestream signature (0xFF 0x0A)
    if bytes.len() >= 2 && bytes[0] == 0xFF && bytes[1] == 0x0A {
        return JxlSignature::Codestream;
    }

    // Check for container signature
    if bytes.len() >= 12 {
        let container_sig: [u8; 12] = [
            0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A,
        ];
        if bytes == container_sig {
            return JxlSignature::Container;
        }
    }

    if size < 12 {
        JxlSignature::NotEnoughBytes
    } else {
        JxlSignature::Invalid
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

fn convert_to_jxl_pixel_format(format: &JxlPixelFormat, num_extra_channels: usize, skip_extra_channels: bool) -> UpstreamPixelFormat {
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

    // If skipping extra channels, set them all to None so they won't be decoded
    let extra_channel_format = if skip_extra_channels {
        vec![None; num_extra_channels]
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
        vec![extra_format; num_extra_channels]
    };

    UpstreamPixelFormat {
        color_type,
        color_data_format: data_format,
        extra_channel_format,
    }
}
