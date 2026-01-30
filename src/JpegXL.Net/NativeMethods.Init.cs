// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Partial class to add static constructor for native library initialization.
/// </summary>
public static partial class NativeMethods
{
    static NativeMethods()
    {
        NativeLibraryLoader.Initialize();
    }
}
