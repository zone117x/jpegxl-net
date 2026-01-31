// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Type conversion functions between C API types and upstream jxl-rs types.

use crate::types::*;
use jxl::api::{Endianness, JxlDecoderOptions};
use jxl::api::JxlProgressiveMode as UpstreamProgressiveMode;
use jxl::api::{
    JxlColorEncoding as UpstreamColorEncoding,
    JxlColorProfile as UpstreamColorProfile,
    JxlPrimaries as UpstreamPrimaries,
    JxlTransferFunction as UpstreamTransferFunction,
    JxlWhitePoint as UpstreamWhitePoint,
};
use jxl::headers::color_encoding::RenderingIntent as UpstreamRenderingIntent;
use jxl::headers::extra_channels::ExtraChannel;
use jxl::headers::image_metadata::Orientation;

// Type aliases for upstream jxl types
pub(crate) type UpstreamPixelFormat = jxl::api::JxlPixelFormat;
pub(crate) type UpstreamColorType = jxl::api::JxlColorType;
pub(crate) type UpstreamDataFormat = jxl::api::JxlDataFormat;

// ============================================================================
// Options Conversion
// ============================================================================

/// Converts C-compatible options to upstream decoder options.
pub(crate) fn convert_options_to_upstream(c_options: &JxlDecodeOptions) -> JxlDecoderOptions {
    let mut options = JxlDecoderOptions::default();
    options.adjust_orientation = c_options.AdjustOrientation;
    options.render_spot_colors = c_options.RenderSpotColors;
    options.coalescing = c_options.Coalescing;
    options.desired_intensity_target = if c_options.DesiredIntensityTarget > 0.0 {
        Some(c_options.DesiredIntensityTarget)
    } else {
        None
    };
    options.skip_preview = c_options.SkipPreview;
    options.progressive_mode = match c_options.ProgressiveMode {
        JxlProgressiveMode::Eager => UpstreamProgressiveMode::Eager,
        JxlProgressiveMode::Pass => UpstreamProgressiveMode::Pass,
        JxlProgressiveMode::FullFrame => UpstreamProgressiveMode::FullFrame,
    };
    options.enable_output = c_options.EnableOutput;
    options.pixel_limit = if c_options.PixelLimit > 0 {
        Some(c_options.PixelLimit)
    } else {
        None
    };
    options.high_precision = c_options.HighPrecision;
    options.premultiply_output = c_options.PremultiplyAlpha;
    options
}

// ============================================================================
// Buffer Size Calculations
// ============================================================================

/// Calculates bytes per sample based on data format.
pub(crate) fn bytes_per_sample(data_format: JxlDataFormat) -> usize {
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
pub(crate) fn calculate_bytes_per_row(info: &JxlBasicInfoRaw, pixel_format: &JxlPixelFormat) -> usize {
    let width = info.Width as usize;
    let bps = bytes_per_sample(pixel_format.DataFormat);
    let spp = samples_per_pixel(pixel_format.ColorType);
    width * spp * bps
}

/// Calculates the required buffer size for the given image info and pixel format.
pub(crate) fn calculate_buffer_size(info: &JxlBasicInfoRaw, pixel_format: &JxlPixelFormat) -> usize {
    let height = info.Height as usize;
    calculate_bytes_per_row(info, pixel_format) * height
}

// ============================================================================
// Type Conversions
// ============================================================================

pub(crate) fn convert_basic_info(info: &jxl::api::JxlBasicInfo) -> JxlBasicInfoRaw {
    let (anim_num, anim_den, anim_loops) = info
        .animation
        .as_ref()
        .map_or((0, 0, 0), |a| (a.tps_numerator, a.tps_denominator, a.num_loops));

    let (preview_w, preview_h) = info.preview_size.unwrap_or((0, 0));

    // Determine bits_per_sample and exponent_bits
    let (bits, exp_bits) = match info.bit_depth {
        jxl::api::JxlBitDepth::Int { bits_per_sample } => (bits_per_sample, 0),
        jxl::api::JxlBitDepth::Float {
            bits_per_sample,
            exponent_bits_per_sample,
        } => (bits_per_sample, exponent_bits_per_sample),
    };

    JxlBasicInfoRaw {
        Width: info.size.0 as u32,
        Height: info.size.1 as u32,
        BitsPerSample: bits,
        ExponentBitsPerSample: exp_bits,
        NumColorChannels: 3, // RGB, grayscale handled by color_type
        NumExtraChannels: info.extra_channels.len() as u32,
        Animation_TpsNumerator: anim_num,
        Animation_TpsDenominator: anim_den,
        Animation_NumLoops: anim_loops,
        Preview_Width: preview_w as u32,
        Preview_Height: preview_h as u32,
        ToneMapping: JxlToneMapping {
            IntensityTarget: info.tone_mapping.intensity_target,
            MinNits: info.tone_mapping.min_nits,
            LinearBelow: info.tone_mapping.linear_below,
            RelativeToMaxDisplay: info.tone_mapping.relative_to_max_display,
        },
        Orientation: convert_orientation(info.orientation),
        AlphaPremultiplied: false, // TODO: Check actual value from extra channels
        IsAnimated: info.animation.is_some(),
        UsesOriginalProfile: info.uses_original_profile,
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

pub(crate) fn convert_frame_header(header: &jxl::api::JxlFrameHeader) -> JxlFrameHeader {
    JxlFrameHeader {
        DurationMs: header.duration.unwrap_or(0.0) as f32,
        FrameWidth: header.size.0 as u32,
        FrameHeight: header.size.1 as u32,
        NameLength: header.name.len() as u32,
    }
}

pub(crate) fn convert_extra_channel_info(channel: &jxl::api::JxlExtraChannel) -> JxlExtraChannelInfo {
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
        ChannelType: channel_type,
        AlphaAssociated: channel.alpha_associated,
    }
}

pub(crate) fn convert_to_jxl_pixel_format(
    format: &JxlPixelFormat,
    extra_channels: &[JxlExtraChannelInfo],
    skip_extra_channels: bool,
) -> UpstreamPixelFormat {
    let color_type = match format.ColorType {
        JxlColorType::Grayscale => UpstreamColorType::Grayscale,
        JxlColorType::GrayscaleAlpha => UpstreamColorType::GrayscaleAlpha,
        JxlColorType::Rgb => UpstreamColorType::Rgb,
        JxlColorType::Rgba => UpstreamColorType::Rgba,
        JxlColorType::Bgr => UpstreamColorType::Bgr,
        JxlColorType::Bgra => UpstreamColorType::Bgra,
    };

    let endianness = match format.Endianness {
        JxlEndianness::Native => Endianness::native(),
        JxlEndianness::LittleEndian => Endianness::LittleEndian,
        JxlEndianness::BigEndian => Endianness::BigEndian,
    };

    let data_format = match format.DataFormat {
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
        format.ColorType,
        JxlColorType::Rgba | JxlColorType::Bgra | JxlColorType::GrayscaleAlpha
    );

    // If skipping extra channels, set them all to None so they won't be decoded
    let extra_channel_format = if skip_extra_channels {
        vec![None; extra_channels.len()]
    } else {
        let extra_format = match format.DataFormat {
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

        extra_channels
            .iter()
            .map(|ec| {
                // If color type includes alpha and this is the first alpha channel, skip it
                // (it's already part of the color output)
                if color_includes_alpha
                    && ec.ChannelType == JxlExtraChannelType::Alpha
                    && !first_alpha_skipped
                {
                    first_alpha_skipped = true;
                    None
                } else {
                    extra_format
                }
            })
            .collect()
    };

    UpstreamPixelFormat {
        color_type,
        color_data_format: data_format,
        extra_channel_format,
    }
}

// ============================================================================
// Color Profile Conversions
// ============================================================================

/// Converts upstream rendering intent to FFI type.
pub(crate) fn convert_rendering_intent(intent: UpstreamRenderingIntent) -> JxlRenderingIntent {
    match intent {
        UpstreamRenderingIntent::Perceptual => JxlRenderingIntent::Perceptual,
        UpstreamRenderingIntent::Relative => JxlRenderingIntent::Relative,
        UpstreamRenderingIntent::Saturation => JxlRenderingIntent::Saturation,
        UpstreamRenderingIntent::Absolute => JxlRenderingIntent::Absolute,
    }
}

/// Converts FFI rendering intent to upstream type.
pub(crate) fn convert_rendering_intent_to_upstream(intent: JxlRenderingIntent) -> UpstreamRenderingIntent {
    match intent {
        JxlRenderingIntent::Perceptual => UpstreamRenderingIntent::Perceptual,
        JxlRenderingIntent::Relative => UpstreamRenderingIntent::Relative,
        JxlRenderingIntent::Saturation => UpstreamRenderingIntent::Saturation,
        JxlRenderingIntent::Absolute => UpstreamRenderingIntent::Absolute,
    }
}

/// Converts upstream white point to FFI type.
pub(crate) fn convert_white_point(wp: &UpstreamWhitePoint) -> JxlWhitePointRaw {
    match wp {
        UpstreamWhitePoint::D65 => JxlWhitePointRaw {
            Tag: JxlWhitePointTag::D65,
            Wx: 0.0,
            Wy: 0.0,
        },
        UpstreamWhitePoint::E => JxlWhitePointRaw {
            Tag: JxlWhitePointTag::E,
            Wx: 0.0,
            Wy: 0.0,
        },
        UpstreamWhitePoint::DCI => JxlWhitePointRaw {
            Tag: JxlWhitePointTag::Dci,
            Wx: 0.0,
            Wy: 0.0,
        },
        UpstreamWhitePoint::Chromaticity { wx, wy } => JxlWhitePointRaw {
            Tag: JxlWhitePointTag::Chromaticity,
            Wx: *wx,
            Wy: *wy,
        },
    }
}

/// Converts FFI white point to upstream type.
pub(crate) fn convert_white_point_to_upstream(wp: &JxlWhitePointRaw) -> UpstreamWhitePoint {
    match wp.Tag {
        JxlWhitePointTag::D65 => UpstreamWhitePoint::D65,
        JxlWhitePointTag::E => UpstreamWhitePoint::E,
        JxlWhitePointTag::Dci => UpstreamWhitePoint::DCI,
        JxlWhitePointTag::Chromaticity => UpstreamWhitePoint::Chromaticity {
            wx: wp.Wx,
            wy: wp.Wy,
        },
    }
}

/// Converts upstream primaries to FFI type.
pub(crate) fn convert_primaries(prim: &UpstreamPrimaries) -> JxlPrimariesRaw {
    match prim {
        UpstreamPrimaries::SRGB => JxlPrimariesRaw {
            Tag: JxlPrimariesTag::Srgb,
            ..Default::default()
        },
        UpstreamPrimaries::BT2100 => JxlPrimariesRaw {
            Tag: JxlPrimariesTag::Bt2100,
            ..Default::default()
        },
        UpstreamPrimaries::P3 => JxlPrimariesRaw {
            Tag: JxlPrimariesTag::P3,
            ..Default::default()
        },
        UpstreamPrimaries::Chromaticities { rx, ry, gx, gy, bx, by } => JxlPrimariesRaw {
            Tag: JxlPrimariesTag::Chromaticities,
            Rx: *rx,
            Ry: *ry,
            Gx: *gx,
            Gy: *gy,
            Bx: *bx,
            By: *by,
        },
    }
}

/// Converts FFI primaries to upstream type.
pub(crate) fn convert_primaries_to_upstream(prim: &JxlPrimariesRaw) -> UpstreamPrimaries {
    match prim.Tag {
        JxlPrimariesTag::Srgb => UpstreamPrimaries::SRGB,
        JxlPrimariesTag::Bt2100 => UpstreamPrimaries::BT2100,
        JxlPrimariesTag::P3 => UpstreamPrimaries::P3,
        JxlPrimariesTag::Chromaticities => UpstreamPrimaries::Chromaticities {
            rx: prim.Rx,
            ry: prim.Ry,
            gx: prim.Gx,
            gy: prim.Gy,
            bx: prim.Bx,
            by: prim.By,
        },
    }
}

/// Converts upstream transfer function to FFI type.
pub(crate) fn convert_transfer_function(tf: &UpstreamTransferFunction) -> JxlTransferFunctionRaw {
    match tf {
        UpstreamTransferFunction::BT709 => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Bt709,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::Linear => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Linear,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::SRGB => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Srgb,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::PQ => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Pq,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::DCI => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Dci,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::HLG => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Hlg,
            Gamma: 0.0,
        },
        UpstreamTransferFunction::Gamma(g) => JxlTransferFunctionRaw {
            Tag: JxlTransferFunctionTag::Gamma,
            Gamma: *g,
        },
    }
}

/// Converts FFI transfer function to upstream type.
pub(crate) fn convert_transfer_function_to_upstream(tf: &JxlTransferFunctionRaw) -> UpstreamTransferFunction {
    match tf.Tag {
        JxlTransferFunctionTag::Bt709 => UpstreamTransferFunction::BT709,
        JxlTransferFunctionTag::Linear => UpstreamTransferFunction::Linear,
        JxlTransferFunctionTag::Srgb => UpstreamTransferFunction::SRGB,
        JxlTransferFunctionTag::Pq => UpstreamTransferFunction::PQ,
        JxlTransferFunctionTag::Dci => UpstreamTransferFunction::DCI,
        JxlTransferFunctionTag::Hlg => UpstreamTransferFunction::HLG,
        JxlTransferFunctionTag::Gamma => UpstreamTransferFunction::Gamma(tf.Gamma),
    }
}

/// Converts upstream color encoding to FFI type.
pub(crate) fn convert_color_encoding(enc: &UpstreamColorEncoding) -> JxlColorEncodingRaw {
    match enc {
        UpstreamColorEncoding::RgbColorSpace {
            white_point,
            primaries,
            transfer_function,
            rendering_intent,
        } => JxlColorEncodingRaw {
            Tag: JxlColorEncodingTag::Rgb,
            WhitePoint: convert_white_point(white_point),
            Primaries: convert_primaries(primaries),
            TransferFunction: convert_transfer_function(transfer_function),
            RenderingIntent: convert_rendering_intent(*rendering_intent),
        },
        UpstreamColorEncoding::GrayscaleColorSpace {
            white_point,
            transfer_function,
            rendering_intent,
        } => JxlColorEncodingRaw {
            Tag: JxlColorEncodingTag::Grayscale,
            WhitePoint: convert_white_point(white_point),
            Primaries: JxlPrimariesRaw::default(),
            TransferFunction: convert_transfer_function(transfer_function),
            RenderingIntent: convert_rendering_intent(*rendering_intent),
        },
        UpstreamColorEncoding::XYB { rendering_intent } => JxlColorEncodingRaw {
            Tag: JxlColorEncodingTag::Xyb,
            WhitePoint: JxlWhitePointRaw::default(),
            Primaries: JxlPrimariesRaw::default(),
            TransferFunction: JxlTransferFunctionRaw::default(),
            RenderingIntent: convert_rendering_intent(*rendering_intent),
        },
    }
}

/// Converts FFI color encoding to upstream type.
pub(crate) fn convert_color_encoding_to_upstream(enc: &JxlColorEncodingRaw) -> UpstreamColorEncoding {
    match enc.Tag {
        JxlColorEncodingTag::Rgb => UpstreamColorEncoding::RgbColorSpace {
            white_point: convert_white_point_to_upstream(&enc.WhitePoint),
            primaries: convert_primaries_to_upstream(&enc.Primaries),
            transfer_function: convert_transfer_function_to_upstream(&enc.TransferFunction),
            rendering_intent: convert_rendering_intent_to_upstream(enc.RenderingIntent),
        },
        JxlColorEncodingTag::Grayscale => UpstreamColorEncoding::GrayscaleColorSpace {
            white_point: convert_white_point_to_upstream(&enc.WhitePoint),
            transfer_function: convert_transfer_function_to_upstream(&enc.TransferFunction),
            rendering_intent: convert_rendering_intent_to_upstream(enc.RenderingIntent),
        },
        JxlColorEncodingTag::Xyb => UpstreamColorEncoding::XYB {
            rendering_intent: convert_rendering_intent_to_upstream(enc.RenderingIntent),
        },
    }
}

/// Converts upstream color profile to FFI type.
/// Returns (JxlColorProfileRaw, Option<Vec<u8>>) where the Vec contains ICC data if present.
pub(crate) fn convert_color_profile(profile: &UpstreamColorProfile) -> (JxlColorProfileRaw, Option<Vec<u8>>) {
    match profile {
        UpstreamColorProfile::Icc(data) => {
            let raw = JxlColorProfileRaw {
                Tag: JxlColorProfileTag::Icc,
                IccLength: data.len(),
                Encoding: JxlColorEncodingRaw::default(),
            };
            (raw, Some(data.clone()))
        }
        UpstreamColorProfile::Simple(encoding) => {
            let raw = JxlColorProfileRaw {
                Tag: JxlColorProfileTag::Simple,
                IccLength: 0,
                Encoding: convert_color_encoding(encoding),
            };
            (raw, None)
        }
    }
}

/// Converts FFI color profile to upstream type.
pub(crate) fn convert_color_profile_to_upstream(raw: &JxlColorProfileRaw, icc_data: Option<&[u8]>) -> UpstreamColorProfile {
    match raw.Tag {
        JxlColorProfileTag::Icc => {
            let data = icc_data.map(|d| d.to_vec()).unwrap_or_default();
            UpstreamColorProfile::Icc(data)
        }
        JxlColorProfileTag::Simple => {
            UpstreamColorProfile::Simple(convert_color_encoding_to_upstream(&raw.Encoding))
        }
    }
}
