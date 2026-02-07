// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Color Management System implementations.

#[cfg(feature = "cms-lcms2")]
mod lcms2_cms {
    use jxl::api::{JxlCms, JxlCmsTransformer, JxlColorEncoding, JxlColorProfile};
    use jxl::error::{Error, Result};
    use jxl::headers::color_encoding::RenderingIntent;
    use lcms2::{
        AllowCache, ColorSpaceSignatureExt, Intent, PixelFormat, Profile, ThreadContext, Transform,
    };

    /// CMS implementation using Little CMS (lcms2).
    pub struct Lcms2Cms;

    impl JxlCms for Lcms2Cms {
        fn initialize_transforms(
            &self,
            n: usize,
            _max_pixels_per_transform: usize,
            input: JxlColorProfile,
            output: JxlColorProfile,
            _intensity_target: f32,
        ) -> Result<(usize, Vec<Box<dyn JxlCmsTransformer + Send>>)> {
            // Convert profiles to ICC
            let input_icc = input
                .try_as_icc()
                .ok_or_else(|| Error::CmsError("Cannot create ICC for input profile".into()))?;
            let output_icc = output
                .try_as_icc()
                .ok_or_else(|| Error::CmsError("Cannot create ICC for output profile".into()))?;

            // Parse profiles once to determine channel counts
            let temp_input_profile = Profile::new_icc(input_icc.as_slice())
                .map_err(|e| Error::CmsError(format!("lcms2 failed to parse input ICC: {e}")))?;
            let temp_output_profile = Profile::new_icc(output_icc.as_slice())
                .map_err(|e| Error::CmsError(format!("lcms2 failed to parse output ICC: {e}")))?;

            let input_channels = temp_input_profile.color_space().channels() as usize;
            let output_channels = temp_output_profile.color_space().channels() as usize;

            let input_format = channels_to_pixel_format(input_channels)?;
            let output_format = channels_to_pixel_format(output_channels)?;
            let intent = rendering_intent_from_profile(&input);

            // Create transforms using ThreadContext for thread safety (implements Send).
            // Use u8 pixel type with PixelFormat describing the actual f32 data layout.
            let mut transforms: Vec<Box<dyn JxlCmsTransformer + Send>> = Vec::with_capacity(n);

            for _ in 0..n {
                let context = ThreadContext::new();

                let input_profile = Profile::new_icc_context(&context, input_icc.as_slice())
                    .map_err(|e| {
                        Error::CmsError(format!("lcms2 failed to parse input ICC: {e}"))
                    })?;
                let output_profile = Profile::new_icc_context(&context, output_icc.as_slice())
                    .map_err(|e| {
                        Error::CmsError(format!("lcms2 failed to parse output ICC: {e}"))
                    })?;

                let transform: Transform<u8, u8, ThreadContext, AllowCache> =
                    Transform::new_context(
                        context,
                        &input_profile,
                        input_format,
                        &output_profile,
                        output_format,
                        intent,
                    )
                    .map_err(|e| {
                        Error::CmsError(format!("lcms2 failed to create transform: {e}"))
                    })?;

                transforms.push(Box::new(Lcms2Transformer {
                    transform,
                    input_channels,
                    output_channels,
                }));
            }

            Ok((output_channels, transforms))
        }
    }

    /// Maps channel count to lcms2 PixelFormat for f32 data.
    fn channels_to_pixel_format(channels: usize) -> Result<PixelFormat> {
        match channels {
            1 => Ok(PixelFormat::GRAY_FLT),
            3 => Ok(PixelFormat::RGB_FLT),
            4 => Ok(PixelFormat::CMYK_FLT),
            _ => Err(Error::CmsError(format!(
                "Unsupported channel count: {channels}"
            ))),
        }
    }

    /// Extracts rendering intent from a color profile.
    /// For Simple profiles, reads from the encoding. For ICC profiles, parses the header.
    fn rendering_intent_from_profile(profile: &JxlColorProfile) -> Intent {
        match profile {
            JxlColorProfile::Simple(encoding) => {
                let ri = match encoding {
                    JxlColorEncoding::RgbColorSpace {
                        rendering_intent, ..
                    } => rendering_intent,
                    JxlColorEncoding::GrayscaleColorSpace {
                        rendering_intent, ..
                    } => rendering_intent,
                    JxlColorEncoding::XYB {
                        rendering_intent, ..
                    } => rendering_intent,
                };
                match ri {
                    RenderingIntent::Perceptual => Intent::Perceptual,
                    RenderingIntent::Relative => Intent::RelativeColorimetric,
                    RenderingIntent::Saturation => Intent::Saturation,
                    RenderingIntent::Absolute => Intent::AbsoluteColorimetric,
                }
            }
            JxlColorProfile::Icc(icc) if icc.len() >= 68 => {
                // ICC header bytes 64-67 contain the rendering intent (big-endian u32)
                match u32::from_be_bytes([icc[64], icc[65], icc[66], icc[67]]) {
                    0 => Intent::Perceptual,
                    1 => Intent::RelativeColorimetric,
                    2 => Intent::Saturation,
                    3 => Intent::AbsoluteColorimetric,
                    _ => Intent::RelativeColorimetric,
                }
            }
            _ => Intent::RelativeColorimetric,
        }
    }

    /// Transformer implementation using lcms2 with ThreadContext for thread safety.
    struct Lcms2Transformer {
        transform: Transform<u8, u8, ThreadContext, AllowCache>,
        input_channels: usize,
        output_channels: usize,
    }

    impl JxlCmsTransformer for Lcms2Transformer {
        fn do_transform(&mut self, input: &[f32], output: &mut [f32]) -> Result<()> {
            if input.len() % self.input_channels != 0 {
                return Err(Error::CmsError(format!(
                    "Input length {} is not divisible by channel count {}",
                    input.len(),
                    self.input_channels
                )));
            }
            let num_pixels = input.len() / self.input_channels;

            let expected_output_len = num_pixels * self.output_channels;
            if output.len() < expected_output_len {
                return Err(Error::CmsError(format!(
                    "Output buffer too small: expected {expected_output_len}, got {}",
                    output.len()
                )));
            }

            let input_bytes: &[u8] = bytemuck::cast_slice(input);
            let output_bytes: &mut [u8] = bytemuck::cast_slice_mut(output);

            self.transform.transform_pixels(input_bytes, output_bytes);

            Ok(())
        }

        fn do_transform_inplace(&mut self, inout: &mut [f32]) -> Result<()> {
            if self.input_channels != self.output_channels {
                return Err(Error::CmsError(
                    "In-place transform requires matching channel counts".into(),
                ));
            }

            let inout_bytes: &mut [u8] = bytemuck::cast_slice_mut(inout);

            self.transform.transform_in_place(inout_bytes);

            Ok(())
        }
    }
}

#[cfg(feature = "cms-lcms2")]
pub(crate) use lcms2_cms::Lcms2Cms;
