// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

//! Error handling for the C API.

use std::cell::RefCell;
use std::ffi::c_char;

thread_local! {
    static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) };
}

/// Sets the last error message for the current thread.
pub(crate) fn set_last_error(msg: impl Into<String>) {
    LAST_ERROR.with(|e| {
        *e.borrow_mut() = msg.into();
    });
}

/// Clears the last error message.
pub(crate) fn clear_last_error() {
    LAST_ERROR.with(|e| {
        e.borrow_mut().clear();
    });
}

/// Gets the last error message.
///
/// # Arguments
/// * `buffer` - Buffer to write the error message to.
/// * `buffer_size` - Size of the buffer in bytes.
///
/// # Returns
/// The length of the error message (excluding null terminator).
/// If the buffer is too small, the message is truncated.
/// Returns 0 if there is no error message.
///
/// # Safety
/// The buffer must be valid for writes of `buffer_size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn jxl_get_last_error(buffer: *mut c_char, buffer_size: usize) -> usize {
    if buffer.is_null() || buffer_size == 0 {
        return LAST_ERROR.with(|e| e.borrow().len());
    }

    LAST_ERROR.with(|e| {
        let error = e.borrow();
        let bytes = error.as_bytes();
        let copy_len = bytes.len().min(buffer_size - 1);

        if copy_len > 0 {
            unsafe {
                std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer as *mut u8, copy_len);
            }
        }

        // Null terminate
        unsafe {
            *buffer.add(copy_len) = 0;
        }

        error.len()
    })
}

/// Clears the last error message.
#[unsafe(no_mangle)]
pub extern "C" fn jxl_clear_last_error() {
    clear_last_error();
}
