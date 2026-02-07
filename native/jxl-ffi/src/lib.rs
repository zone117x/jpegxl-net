// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! jxlrs - C API for jxl-rs JPEG XL decoder.
//!
//! This crate provides a C-compatible API for decoding JPEG XL images,
//! designed for FFI bindings to languages like C#.

mod cms;
mod conversions;
mod decoder;
mod error;
mod types;

pub use decoder::*;
pub use error::*;
pub use types::*;

/// Returns the library version as a packed integer.
/// Format: (major << 24) | (minor << 16) | (patch << 8)
#[unsafe(no_mangle)]
pub extern "C" fn jxl_version() -> u32 {
    let major: u32 = env!("CARGO_PKG_VERSION_MAJOR").parse().unwrap_or(0);
    let minor: u32 = env!("CARGO_PKG_VERSION_MINOR").parse().unwrap_or(0);
    let patch: u32 = env!("CARGO_PKG_VERSION_PATCH").parse().unwrap_or(0);
    (major << 24) | (minor << 16) | (patch << 8)
}
