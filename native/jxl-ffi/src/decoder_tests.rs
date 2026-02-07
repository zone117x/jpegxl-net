// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Unit tests for the decoder module.

use super::*;

#[test]
fn test_convert_to_jxl_pixel_format_rgba_with_alpha() {
    let format = JxlPixelFormat {
        ColorType: JxlColorType::Rgba,
        DataFormat: JxlDataFormat::Uint8,
        Endianness: JxlEndianness::Native,
    };

    let extra_channels = vec![JxlExtraChannelInfo {
        ChannelType: JxlExtraChannelType::Alpha,
        AlphaAssociated: false,
    }];

    // When using RGBA with alpha as extra channel, alpha should be None
    // (alpha is already in the RGBA color output)
    let pixel_format = convert_to_jxl_pixel_format(&format, &extra_channels, false);

    assert_eq!(pixel_format.extra_channel_format.len(), 1);
    assert!(
        pixel_format.extra_channel_format[0].is_none(),
        "Alpha should be None when using RGBA"
    );
}

#[test]
fn test_convert_to_jxl_pixel_format_rgb_with_alpha() {
    let format = JxlPixelFormat {
        ColorType: JxlColorType::Rgb,
        DataFormat: JxlDataFormat::Uint8,
        Endianness: JxlEndianness::Native,
    };

    let extra_channels = vec![JxlExtraChannelInfo {
        ChannelType: JxlExtraChannelType::Alpha,
        AlphaAssociated: false,
    }];

    // When using RGB (no alpha in color), alpha should be Some
    // (alpha needs to go to a separate buffer)
    let pixel_format = convert_to_jxl_pixel_format(&format, &extra_channels, false);

    assert_eq!(pixel_format.extra_channel_format.len(), 1);
    assert!(
        pixel_format.extra_channel_format[0].is_some(),
        "Alpha should be Some when using RGB"
    );
}
