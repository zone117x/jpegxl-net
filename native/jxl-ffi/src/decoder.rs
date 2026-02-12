// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Decoder implementation for the C API.

use crate::conversions::{
    bytes_per_sample, calculate_buffer_size, calculate_bytes_per_row, convert_basic_info,
    convert_color_encoding, convert_color_encoding_to_upstream, convert_color_profile,
    convert_extra_channel_info, convert_frame_header, convert_options_to_upstream,
    convert_to_jxl_pixel_format, convert_transfer_function,
};
use crate::error::{clear_last_error, set_last_error};
use crate::types::*;
use jxl::api::{JxlColorProfile, ProcessingResult};
use jxl::image::JxlOutputBuffer;
use std::ffi::CStr;
use std::os::raw::c_char;
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

/// Cached metadata box with compression flag.
struct CachedMetadataBox {
    data: Vec<u8>,
    is_brotli_compressed: bool,
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
    /// CMS type to use for color management.
    cms_type: JxlCmsType,
    /// Cached EXIF boxes (avoids re-cloning on repeated access).
    exif_boxes_cache: Option<Vec<CachedMetadataBox>>,
    /// Cached XML boxes (avoids re-cloning on repeated access).
    xml_boxes_cache: Option<Vec<CachedMetadataBox>>,
    /// Cached JUMBF boxes (avoids re-cloning on repeated access).
    jumbf_boxes_cache: Option<Vec<CachedMetadataBox>>,
}

impl DecoderInner {
    fn new() -> Self {
        Self::with_options(JxlDecodeOptions::default())
    }

    fn with_options(options: JxlDecodeOptions) -> Self {
        let cms_type = options.CmsType;
        let mut upstream_opts = convert_options_to_upstream(&options);
        upstream_opts.cms = create_cms(cms_type);
        Self {
            state: DecoderState::Initialized(UpstreamDecoder::new(upstream_opts)),
            data: Vec::new(),
            data_offset: 0,
            basic_info: None,
            extra_channels: Vec::new(),
            pixel_format: options.PixelFormat,
            options,
            cms_type,
            exif_boxes_cache: None,
            xml_boxes_cache: None,
            jumbf_boxes_cache: None,
        }
    }

    fn reset(&mut self) {
        self.reset_state();
        self.data.clear();
        self.data_offset = 0;
        self.basic_info = None;
        self.extra_channels.clear();
        self.exif_boxes_cache = None;
        self.xml_boxes_cache = None;
        self.jumbf_boxes_cache = None;
    }

    /// Rewinds the decoder to the beginning of the input without clearing the data buffer.
    /// This allows re-decoding the same input without calling SetInput again.
    fn rewind(&mut self) {
        self.reset_state();
        self.data_offset = 0;
        self.basic_info = None;
        self.extra_channels.clear();
        self.exif_boxes_cache = None;
        self.xml_boxes_cache = None;
        self.jumbf_boxes_cache = None;
    }

    /// Resets only the decoder state (used for error recovery).
    fn reset_state(&mut self) {
        let mut opts = convert_options_to_upstream(&self.options);
        opts.cms = create_cms(self.cms_type);
        self.state = DecoderState::Initialized(UpstreamDecoder::new(opts));
    }
}

/// Creates a CMS implementation from the given type.
fn create_cms(cms_type: JxlCmsType) -> Option<Box<dyn jxl::api::JxlCms>> {
    match cms_type {
        JxlCmsType::None => None,
        #[cfg(feature = "cms-lcms2")]
        JxlCmsType::Lcms2 => Some(Box::new(crate::cms::Lcms2Cms)),
        #[cfg(not(feature = "cms-lcms2"))]
        JxlCmsType::Lcms2 => {
            set_last_error("lcms2 support not compiled in");
            None
        }
        #[cfg(feature = "tone-mapping")]
        JxlCmsType::Bt2446a => Some(Box::new(crate::cms::ToneMappingLcms2Cms {
            desired_intensity_target: 203.0,
            method: crate::tone_mapping::ToneMapMethod::Bt2446a,
        })),
        #[cfg(feature = "tone-mapping")]
        JxlCmsType::Bt2446aLinear => Some(Box::new(crate::cms::ToneMappingLcms2Cms {
            desired_intensity_target: 203.0,
            method: crate::tone_mapping::ToneMapMethod::Bt2446aLinear,
        })),
        #[cfg(feature = "tone-mapping")]
        JxlCmsType::Bt2446aPerceptual => Some(Box::new(crate::cms::ToneMappingLcms2Cms {
            desired_intensity_target: 203.0,
            method: crate::tone_mapping::ToneMapMethod::Bt2446aPerceptual,
        })),
        #[cfg(not(feature = "tone-mapping"))]
        JxlCmsType::Bt2446a | JxlCmsType::Bt2446aLinear | JxlCmsType::Bt2446aPerceptual => {
            set_last_error("tone-mapping support not compiled in");
            None
        }
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

/// Sets input data by reading directly from a file.
///
/// This is more efficient than reading the file in managed code and then
/// calling `jxl_decoder_append_input`, as it avoids an intermediate copy.
///
/// # Safety
/// - `decoder` must be a valid decoder pointer.
/// - `path` must be a valid null-terminated UTF-8 string.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_set_input_file(
    decoder: *mut NativeDecoderHandle,
    path: *const c_char,
) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    if path.is_null() {
        set_last_error("Null path pointer");
        return JxlStatus::InvalidArgument;
    }

    let path_str = match unsafe { CStr::from_ptr(path) }.to_str() {
        Ok(s) => s,
        Err(_) => {
            set_last_error("Invalid UTF-8 in file path");
            return JxlStatus::InvalidArgument;
        }
    };

    clear_last_error();

    match std::fs::read(path_str) {
        Ok(data) => {
            inner.reset();
            inner.data = data;
            JxlStatus::Success
        }
        Err(e) => {
            set_last_error(&format!("Failed to read file '{}': {}", path_str, e));
            JxlStatus::IoError
        }
    }
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
        *out_header = convert_frame_header(&jxl_header);
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
// Color Profiles
// ============================================================================

/// Internal structure to hold a cloned color profile for FFI access.
struct ColorProfileHandle {
    profile: JxlColorProfile,
    /// Cached ICC data (if profile is ICC type)
    icc_cache: Option<Vec<u8>>,
}

/// Creates a new color profile handle from an existing profile.
/// The handle must be freed with `jxl_color_profile_free`.
fn create_profile_handle(profile: JxlColorProfile) -> *mut JxlColorProfileHandle {
    let icc_cache = match &profile {
        JxlColorProfile::Icc(data) => Some(data.clone()),
        JxlColorProfile::Simple(_) => None,
    };
    let handle = Box::new(ColorProfileHandle { profile, icc_cache });
    Box::into_raw(handle) as *mut JxlColorProfileHandle
}

/// Gets the embedded color profile from the image.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `profile_out` - Output for the profile raw data.
/// * `icc_data_out` - Output pointer for ICC data (only set if profile is ICC type).
/// * `handle_out` - Output for the profile handle (for calling helper methods).
///
/// # Safety
/// - `decoder` must be valid.
/// - `profile_out` must point to a writable `JxlColorProfileRaw`.
/// - `icc_data_out` must point to a writable pointer.
/// - `handle_out` must point to a writable pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_embedded_color_profile(
    decoder: *const NativeDecoderHandle,
    profile_out: *mut JxlColorProfileRaw,
    icc_data_out: *mut *const u8,
    handle_out: *mut *mut JxlColorProfileHandle,
) -> JxlStatus {
    let inner = get_decoder_ref!(decoder, JxlStatus::InvalidArgument);

    let profile = match &inner.state {
        DecoderState::WithImageInfo(d) => d.embedded_color_profile(),
        DecoderState::WithFrameInfo(_) => {
            set_last_error("Color profile not accessible in WithFrameInfo state");
            return JxlStatus::InvalidState;
        }
        _ => {
            set_last_error("Basic info not yet available - call jxl_decoder_process first");
            return JxlStatus::InvalidState;
        }
    };

    clear_last_error();

    // Convert profile to raw format
    let (raw, _icc_data) = convert_color_profile(profile);

    // Create a handle with cloned profile
    let handle = create_profile_handle(profile.clone());

    // Write outputs
    if let Some(out) = unsafe { profile_out.as_mut() } {
        *out = raw;
    }

    if let Some(out) = unsafe { icc_data_out.as_mut() } {
        // Get ICC data from handle's cache
        let handle_ref = unsafe { &*(handle as *const ColorProfileHandle) };
        *out = handle_ref.icc_cache.as_ref()
            .map(|v| v.as_ptr())
            .unwrap_or(std::ptr::null());
    }

    if let Some(out) = unsafe { handle_out.as_mut() } {
        *out = handle;
    } else {
        // If no handle output, free it
        unsafe { drop(Box::from_raw(handle as *mut ColorProfileHandle)) };
    }

    JxlStatus::Success
}

/// Gets the current output color profile.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Safety
/// Same as `jxl_decoder_get_embedded_color_profile`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_output_color_profile(
    decoder: *const NativeDecoderHandle,
    profile_out: *mut JxlColorProfileRaw,
    icc_data_out: *mut *const u8,
    handle_out: *mut *mut JxlColorProfileHandle,
) -> JxlStatus {
    let inner = get_decoder_ref!(decoder, JxlStatus::InvalidArgument);

    let profile = match &inner.state {
        DecoderState::WithImageInfo(d) => d.output_color_profile(),
        DecoderState::WithFrameInfo(_) => {
            set_last_error("Color profile not accessible in WithFrameInfo state");
            return JxlStatus::InvalidState;
        }
        _ => {
            set_last_error("Basic info not yet available - call jxl_decoder_process first");
            return JxlStatus::InvalidState;
        }
    };

    clear_last_error();

    let (raw, _icc_data) = convert_color_profile(profile);
    let handle = create_profile_handle(profile.clone());

    if let Some(out) = unsafe { profile_out.as_mut() } {
        *out = raw;
    }

    if let Some(out) = unsafe { icc_data_out.as_mut() } {
        let handle_ref = unsafe { &*(handle as *const ColorProfileHandle) };
        *out = handle_ref.icc_cache.as_ref()
            .map(|v| v.as_ptr())
            .unwrap_or(std::ptr::null());
    }

    if let Some(out) = unsafe { handle_out.as_mut() } {
        *out = handle;
    } else {
        unsafe { drop(Box::from_raw(handle as *mut ColorProfileHandle)) };
    }

    JxlStatus::Success
}

/// Sets the output color profile for decoding.
///
/// Must be called after `HaveBasicInfo` and before decoding pixels.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `profile` - The color profile raw data.
/// * `icc_data` - ICC data pointer (required if profile tag is Icc).
///
/// # Safety
/// - `decoder` must be valid.
/// - `profile` must point to a valid `JxlColorProfileRaw`.
/// - If profile is ICC, `icc_data` must point to `profile.IccLength` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_set_output_color_profile(
    decoder: *mut NativeDecoderHandle,
    profile: *const JxlColorProfileRaw,
    icc_data: *const u8,
) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    let Some(raw) = (unsafe { profile.as_ref() }) else {
        set_last_error("Null profile pointer");
        return JxlStatus::InvalidArgument;
    };

    // Convert raw to upstream profile
    let icc_slice = if raw.Tag == JxlColorProfileTag::Icc && raw.IccLength > 0 {
        if icc_data.is_null() {
            set_last_error("ICC profile specified but icc_data is null");
            return JxlStatus::InvalidArgument;
        }
        Some(unsafe { slice::from_raw_parts(icc_data, raw.IccLength) })
    } else {
        None
    };

    let upstream_profile = crate::conversions::convert_color_profile_to_upstream(raw, icc_slice);

    // Set the profile on the decoder
    let state = std::mem::replace(&mut inner.state, DecoderState::Processing);

    match state {
        DecoderState::WithImageInfo(mut d) => {
            match d.set_output_color_profile(upstream_profile) {
                Ok(()) => {
                    clear_last_error();
                    inner.state = DecoderState::WithImageInfo(d);
                    JxlStatus::Success
                }
                Err(e) => {
                    inner.state = DecoderState::WithImageInfo(d);
                    set_last_error(format!("Failed to set output color profile: {}", e));
                    JxlStatus::Error
                }
            }
        }
        other => {
            inner.state = other;
            set_last_error("Must be in WithImageInfo state to set output color profile");
            JxlStatus::InvalidState
        }
    }
}

/// Frees a color profile handle.
///
/// # Safety
/// The handle must have been created by a color profile function.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_free(handle: *mut JxlColorProfileHandle) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut ColorProfileHandle)) };
    }
}

/// Clones a color profile handle.
///
/// # Returns
/// A new handle that must be freed with `jxl_color_profile_free`, or null on failure.
///
/// # Safety
/// The handle must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_clone(
    handle: *const JxlColorProfileHandle,
) -> *mut JxlColorProfileHandle {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return std::ptr::null_mut();
    };

    create_profile_handle(inner.profile.clone())
}

/// Attempts to get ICC profile data from a color profile.
///
/// Returns true if ICC data is available (either native or converted).
///
/// # Arguments
/// * `handle` - The color profile handle.
/// * `data_out` - Output pointer for ICC data.
/// * `length_out` - Output for ICC data length.
///
/// # Safety
/// - `handle` must be valid.
/// - `data_out` and `length_out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_try_as_icc(
    handle: *mut JxlColorProfileHandle,
    data_out: *mut *const u8,
    length_out: *mut usize,
) -> bool {
    let Some(inner) = (unsafe { (handle as *mut ColorProfileHandle).as_mut() }) else {
        return false;
    };

    // Try to get ICC data
    match inner.profile.try_as_icc() {
        Some(cow) => {
            // Cache the ICC data if it was generated
            if inner.icc_cache.is_none() {
                inner.icc_cache = Some(cow.into_owned());
            }

            if let Some(ref data) = inner.icc_cache {
                if let Some(out) = unsafe { data_out.as_mut() } {
                    *out = data.as_ptr();
                }
                if let Some(out) = unsafe { length_out.as_mut() } {
                    *out = data.len();
                }
                true
            } else {
                false
            }
        }
        None => false,
    }
}

/// Gets the number of color channels for a profile.
///
/// # Returns
/// 1 for grayscale, 3 for RGB, 4 for CMYK.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_channels(
    handle: *const JxlColorProfileHandle,
) -> u32 {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return 0;
    };

    inner.profile.channels() as u32
}

/// Checks if a profile represents a CMYK color space.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_is_cmyk(
    handle: *const JxlColorProfileHandle,
) -> bool {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return false;
    };

    inner.profile.is_cmyk()
}

/// Checks if the decoder can output to this profile without a CMS.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_can_output_to(
    handle: *const JxlColorProfileHandle,
) -> bool {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return false;
    };

    inner.profile.can_output_to()
}

/// Checks if two profiles represent the same color encoding.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_same_color_encoding(
    handle_a: *const JxlColorProfileHandle,
    handle_b: *const JxlColorProfileHandle,
) -> bool {
    let (Some(a), Some(b)) = (
        unsafe { (handle_a as *const ColorProfileHandle).as_ref() },
        unsafe { (handle_b as *const ColorProfileHandle).as_ref() },
    ) else {
        return false;
    };

    a.profile.same_color_encoding(&b.profile)
}

/// Creates a copy of a profile with linear transfer function.
///
/// # Returns
/// A new handle, or null if not possible (e.g., for ICC profiles).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_with_linear_tf(
    handle: *const JxlColorProfileHandle,
) -> *mut JxlColorProfileHandle {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return std::ptr::null_mut();
    };

    match inner.profile.with_linear_tf() {
        Some(new_profile) => create_profile_handle(new_profile),
        None => std::ptr::null_mut(),
    }
}

/// Gets the transfer function from a simple color profile.
///
/// # Returns
/// True if the profile has a transfer function, false otherwise (ICC or XYB).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_get_transfer_function(
    handle: *const JxlColorProfileHandle,
    tf_out: *mut JxlTransferFunctionRaw,
) -> bool {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return false;
    };

    match inner.profile.transfer_function() {
        Some(tf) => {
            if let Some(out) = unsafe { tf_out.as_mut() } {
                *out = convert_transfer_function(tf);
            }
            true
        }
        None => false,
    }
}

/// Gets the string representation of a color profile.
///
/// # Arguments
/// * `handle` - The color profile handle.
/// * `buffer` - Output buffer for the string, or null to query required size.
/// * `buffer_size` - Size of the buffer in bytes.
///
/// # Returns
/// The number of bytes written (excluding null terminator), or required size if buffer is null/too small.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_to_string(
    handle: *const JxlColorProfileHandle,
    buffer: *mut u8,
    buffer_size: usize,
) -> usize {
    let Some(inner) = (unsafe { (handle as *const ColorProfileHandle).as_ref() }) else {
        return 0;
    };

    let s = format!("{}", inner.profile);
    let bytes = s.as_bytes();

    if buffer.is_null() || buffer_size < bytes.len() {
        return bytes.len();
    }

    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer, bytes.len());
    }

    bytes.len()
}

/// Gets the description string for a color encoding.
///
/// This returns human-readable names like "sRGB", "DisplayP3", "Rec2100PQ" for known
/// profiles, or a detailed encoding string for custom profiles.
///
/// # Returns
/// The number of bytes written, or required size if buffer is null/too small.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_encoding_get_description(
    encoding: *const JxlColorEncodingRaw,
    buffer: *mut u8,
    buffer_size: usize,
) -> usize {
    let Some(raw) = (unsafe { encoding.as_ref() }) else {
        return 0;
    };

    let upstream = convert_color_encoding_to_upstream(raw);
    let s = upstream.get_color_encoding_description();
    let bytes = s.as_bytes();

    if buffer.is_null() || buffer_size < bytes.len() {
        return bytes.len();
    }

    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer, bytes.len());
    }

    bytes.len()
}

/// Creates a color profile handle from a simple color encoding.
///
/// # Returns
/// A new handle that must be freed with `jxl_color_profile_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_from_encoding(
    encoding: *const JxlColorEncodingRaw,
) -> *mut JxlColorProfileHandle {
    let Some(raw) = (unsafe { encoding.as_ref() }) else {
        return std::ptr::null_mut();
    };

    let upstream = convert_color_encoding_to_upstream(raw);
    create_profile_handle(JxlColorProfile::Simple(upstream))
}

/// Creates a color profile handle from ICC data.
///
/// # Safety
/// `icc_data` must point to `icc_length` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_profile_from_icc(
    icc_data: *const u8,
    icc_length: usize,
) -> *mut JxlColorProfileHandle {
    if icc_data.is_null() || icc_length == 0 {
        return std::ptr::null_mut();
    }

    let data = unsafe { slice::from_raw_parts(icc_data, icc_length) }.to_vec();
    create_profile_handle(JxlColorProfile::Icc(data))
}

/// Creates a standard sRGB color encoding.
///
/// # Arguments
/// * `grayscale` - If true, creates grayscale sRGB; otherwise RGB sRGB.
/// * `encoding_out` - Output for the encoding data.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_encoding_srgb(
    grayscale: bool,
    encoding_out: *mut JxlColorEncodingRaw,
) {
    let encoding = jxl::api::JxlColorEncoding::srgb(grayscale);
    if let Some(out) = unsafe { encoding_out.as_mut() } {
        *out = convert_color_encoding(&encoding);
    }
}

/// Creates a linear sRGB color encoding.
///
/// # Arguments
/// * `grayscale` - If true, creates grayscale linear sRGB; otherwise RGB linear sRGB.
/// * `encoding_out` - Output for the encoding data.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_color_encoding_linear_srgb(
    grayscale: bool,
    encoding_out: *mut JxlColorEncodingRaw,
) {
    let encoding = jxl::api::JxlColorEncoding::linear_srgb(grayscale);
    if let Some(out) = unsafe { encoding_out.as_mut() } {
        *out = convert_color_encoding(&encoding);
    }
}

// ============================================================================
// Metadata Box Access
// ============================================================================

/// Gets the number of EXIF boxes in the image.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Returns
/// The number of EXIF boxes, or 0 if none or not accessible.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_exif_box_count(
    decoder: *const NativeDecoderHandle,
) -> u32 {
    let inner = get_decoder_ref_silent!(decoder, 0);

    match &inner.state {
        DecoderState::WithImageInfo(d) => {
            d.exif_boxes().map_or(0, |boxes| boxes.len() as u32)
        }
        _ => 0,
    }
}

/// Gets the number of XML/XMP boxes in the image.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Returns
/// The number of XML boxes, or 0 if none or not accessible.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_xml_box_count(
    decoder: *const NativeDecoderHandle,
) -> u32 {
    let inner = get_decoder_ref_silent!(decoder, 0);

    match &inner.state {
        DecoderState::WithImageInfo(d) => {
            d.xmp_boxes().map_or(0, |boxes| boxes.len() as u32)
        }
        _ => 0,
    }
}

/// Gets the number of JUMBF boxes in the image.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
///
/// # Returns
/// The number of JUMBF boxes, or 0 if none or not accessible.
///
/// # Safety
/// The decoder pointer must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_jumbf_box_count(
    decoder: *const NativeDecoderHandle,
) -> u32 {
    let inner = get_decoder_ref_silent!(decoder, 0);

    match &inner.state {
        DecoderState::WithImageInfo(d) => {
            d.jumbf_boxes().map_or(0, |boxes| boxes.len() as u32)
        }
        _ => 0,
    }
}

/// Gets EXIF data from a specific box by index.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
/// The returned pointer is valid until the decoder is reset, rewound, or freed.
///
/// # Arguments
/// * `decoder` - The decoder instance (mutable for caching).
/// * `index` - Zero-based box index.
/// * `data_out` - Output pointer for EXIF data bytes.
/// * `length_out` - Output for EXIF data length.
/// * `is_brotli_compressed` - Output for brotli compression flag (true if brob box).
///
/// # Returns
/// - `Success` if EXIF data is available.
/// - `InvalidState` if called before basic info is available.
/// - `InvalidArgument` if index is out of range.
/// - `Error` if no EXIF data exists in the image.
///
/// # Safety
/// - `decoder` must be valid.
/// - Output pointers must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_exif_box_at(
    decoder: *mut NativeDecoderHandle,
    index: u32,
    data_out: *mut *const u8,
    length_out: *mut usize,
    is_brotli_compressed: *mut bool,
) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    // Populate cache if needed
    if inner.exif_boxes_cache.is_none() {
        let boxes = match &inner.state {
            DecoderState::WithImageInfo(d) => d.exif_boxes(),
            _ => {
                set_last_error("EXIF data not accessible - call jxl_decoder_process until HaveBasicInfo");
                return JxlStatus::InvalidState;
            }
        };

        let Some(boxes) = boxes else {
            set_last_error("Image does not contain EXIF data");
            return JxlStatus::Error;
        };

        if boxes.is_empty() {
            set_last_error("Image does not contain EXIF data");
            return JxlStatus::Error;
        }

        // Cache all boxes with compression flag
        inner.exif_boxes_cache = Some(
            boxes
                .iter()
                .map(|b| CachedMetadataBox {
                    data: b.data.clone(),
                    is_brotli_compressed: b.is_brotli_compressed,
                })
                .collect(),
        );
    }

    let cached = inner.exif_boxes_cache.as_ref().unwrap();
    let idx = index as usize;

    if idx >= cached.len() {
        set_last_error(format!("EXIF box index {} out of range (max {})", index, cached.len() - 1));
        return JxlStatus::InvalidArgument;
    }

    clear_last_error();

    let cached_box = &cached[idx];

    if let Some(out) = unsafe { data_out.as_mut() } {
        *out = cached_box.data.as_ptr();
    }
    if let Some(out) = unsafe { length_out.as_mut() } {
        *out = cached_box.data.len();
    }
    if let Some(out) = unsafe { is_brotli_compressed.as_mut() } {
        *out = cached_box.is_brotli_compressed;
    }

    JxlStatus::Success
}

/// Gets XML/XMP data from a specific box by index.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
/// The returned pointer is valid until the decoder is reset, rewound, or freed.
///
/// # Arguments
/// * `decoder` - The decoder instance (mutable for caching).
/// * `index` - Zero-based box index.
/// * `data_out` - Output pointer for XML data bytes.
/// * `length_out` - Output for XML data length.
/// * `is_brotli_compressed` - Output for brotli compression flag (true if brob box).
///
/// # Returns
/// - `Success` if XML data is available.
/// - `InvalidState` if called before basic info is available.
/// - `InvalidArgument` if index is out of range.
/// - `Error` if no XML data exists in the image.
///
/// # Safety
/// - `decoder` must be valid.
/// - Output pointers must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_xml_box_at(
    decoder: *mut NativeDecoderHandle,
    index: u32,
    data_out: *mut *const u8,
    length_out: *mut usize,
    is_brotli_compressed: *mut bool,
) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    // Populate cache if needed
    if inner.xml_boxes_cache.is_none() {
        let boxes = match &inner.state {
            DecoderState::WithImageInfo(d) => d.xmp_boxes(),
            _ => {
                set_last_error("XML data not accessible - call jxl_decoder_process until HaveBasicInfo");
                return JxlStatus::InvalidState;
            }
        };

        let Some(boxes) = boxes else {
            set_last_error("Image does not contain XML data");
            return JxlStatus::Error;
        };

        if boxes.is_empty() {
            set_last_error("Image does not contain XML data");
            return JxlStatus::Error;
        }

        // Cache all boxes with compression flag
        inner.xml_boxes_cache = Some(
            boxes
                .iter()
                .map(|b| CachedMetadataBox {
                    data: b.data.clone(),
                    is_brotli_compressed: b.is_brotli_compressed,
                })
                .collect(),
        );
    }

    let cached = inner.xml_boxes_cache.as_ref().unwrap();
    let idx = index as usize;

    if idx >= cached.len() {
        set_last_error(format!("XML box index {} out of range (max {})", index, cached.len() - 1));
        return JxlStatus::InvalidArgument;
    }

    clear_last_error();

    let cached_box = &cached[idx];

    if let Some(out) = unsafe { data_out.as_mut() } {
        *out = cached_box.data.as_ptr();
    }
    if let Some(out) = unsafe { length_out.as_mut() } {
        *out = cached_box.data.len();
    }
    if let Some(out) = unsafe { is_brotli_compressed.as_mut() } {
        *out = cached_box.is_brotli_compressed;
    }

    JxlStatus::Success
}

/// Gets JUMBF data from a specific box by index.
///
/// Only valid after `jxl_decoder_process` returns `HaveBasicInfo`.
/// The returned pointer is valid until the decoder is reset, rewound, or freed.
///
/// # Arguments
/// * `decoder` - The decoder instance (mutable for caching).
/// * `index` - Zero-based box index.
/// * `data_out` - Output pointer for JUMBF data bytes.
/// * `length_out` - Output for JUMBF data length.
/// * `is_brotli_compressed` - Output for brotli compression flag (true if brob box).
///
/// # Returns
/// - `Success` if JUMBF data is available.
/// - `InvalidState` if called before basic info is available.
/// - `InvalidArgument` if index is out of range.
/// - `Error` if no JUMBF data exists in the image.
///
/// # Safety
/// - `decoder` must be valid.
/// - Output pointers must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_get_jumbf_box_at(
    decoder: *mut NativeDecoderHandle,
    index: u32,
    data_out: *mut *const u8,
    length_out: *mut usize,
    is_brotli_compressed: *mut bool,
) -> JxlStatus {
    let inner = get_decoder_mut!(decoder, JxlStatus::InvalidArgument);

    // Populate cache if needed
    if inner.jumbf_boxes_cache.is_none() {
        let boxes = match &inner.state {
            DecoderState::WithImageInfo(d) => d.jumbf_boxes(),
            _ => {
                set_last_error("JUMBF data not accessible - call jxl_decoder_process until HaveBasicInfo");
                return JxlStatus::InvalidState;
            }
        };

        let Some(boxes) = boxes else {
            set_last_error("Image does not contain JUMBF data");
            return JxlStatus::Error;
        };

        if boxes.is_empty() {
            set_last_error("Image does not contain JUMBF data");
            return JxlStatus::Error;
        }

        // Cache all boxes with compression flag
        inner.jumbf_boxes_cache = Some(
            boxes
                .iter()
                .map(|b| CachedMetadataBox {
                    data: b.data.clone(),
                    is_brotli_compressed: b.is_brotli_compressed,
                })
                .collect(),
        );
    }

    let cached = inner.jumbf_boxes_cache.as_ref().unwrap();
    let idx = index as usize;

    if idx >= cached.len() {
        set_last_error(format!("JUMBF box index {} out of range (max {})", index, cached.len() - 1));
        return JxlStatus::InvalidArgument;
    }

    clear_last_error();

    let cached_box = &cached[idx];

    if let Some(out) = unsafe { data_out.as_mut() } {
        *out = cached_box.data.as_ptr();
    }
    if let Some(out) = unsafe { length_out.as_mut() } {
        *out = cached_box.data.len();
    }
    if let Some(out) = unsafe { is_brotli_compressed.as_mut() } {
        *out = cached_box.is_brotli_compressed;
    }

    JxlStatus::Success
}

// ============================================================================
// Metadata Box Compression Status (deprecated - use get_*_box_at with is_brotli_compressed)
// ============================================================================

/// Returns whether the EXIF box at the given index is brotli-compressed.
///
/// Only valid after `jxl_decoder_get_exif_box_at` has been called to populate the cache.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `index` - Zero-based box index.
///
/// # Returns
/// - `true` if the box was brotli-compressed in the file (brob box).
/// - `false` if uncompressed or if cache not populated.
///
/// # Safety
/// - `decoder` must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_is_exif_box_compressed(
    decoder: *const NativeDecoderHandle,
    index: u32,
) -> bool {
    let inner = get_decoder_ref_silent!(decoder, false);
    inner
        .exif_boxes_cache
        .as_ref()
        .and_then(|boxes| boxes.get(index as usize))
        .map(|b| b.is_brotli_compressed)
        .unwrap_or(false)
}

/// Returns whether the XML box at the given index is brotli-compressed.
///
/// Only valid after `jxl_decoder_get_xml_box_at` has been called to populate the cache.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `index` - Zero-based box index.
///
/// # Returns
/// - `true` if the box was brotli-compressed in the file (brob box).
/// - `false` if uncompressed or if cache not populated.
///
/// # Safety
/// - `decoder` must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_is_xml_box_compressed(
    decoder: *const NativeDecoderHandle,
    index: u32,
) -> bool {
    let inner = get_decoder_ref_silent!(decoder, false);
    inner
        .xml_boxes_cache
        .as_ref()
        .and_then(|boxes| boxes.get(index as usize))
        .map(|b| b.is_brotli_compressed)
        .unwrap_or(false)
}

/// Returns whether the JUMBF box at the given index is brotli-compressed.
///
/// Only valid after `jxl_decoder_get_jumbf_box_at` has been called to populate the cache.
///
/// # Arguments
/// * `decoder` - The decoder instance.
/// * `index` - Zero-based box index.
///
/// # Returns
/// - `true` if the box was brotli-compressed in the file (brob box).
/// - `false` if uncompressed or if cache not populated.
///
/// # Safety
/// - `decoder` must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_decoder_is_jumbf_box_compressed(
    decoder: *const NativeDecoderHandle,
    index: u32,
) -> bool {
    let inner = get_decoder_ref_silent!(decoder, false);
    inner
        .jumbf_boxes_cache
        .as_ref()
        .and_then(|boxes| boxes.get(index as usize))
        .map(|b| b.is_brotli_compressed)
        .unwrap_or(false)
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