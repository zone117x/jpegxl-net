// Static constructor to initialize the native library resolver
using System;
using System.Runtime.InteropServices;

namespace JpegXL.Net.Native;

internal static unsafe partial class NativeMethods
{
    static NativeMethods()
    {
        NativeLibraryLoader.Initialize();
    }
}
