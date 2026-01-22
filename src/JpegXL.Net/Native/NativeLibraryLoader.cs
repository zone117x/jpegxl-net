using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace JpegXL.Net.Native;

/// <summary>
/// Handles loading the native jxlrs library from the runtimes folder.
/// </summary>
internal static class NativeLibraryLoader
{
    private static bool _initialized;
    private static readonly object _lock = new();
#if NET5_0_OR_GREATER
    private static IntPtr _cachedHandle;
#endif

    /// <summary>
    /// Initializes the native library resolver. Call this before any P/Invoke calls.
    /// </summary>
    internal static void Initialize()
    {
        if (_initialized) return;
        
        lock (_lock)
        {
            if (_initialized) return;
            
#if NET5_0_OR_GREATER
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, ResolveLibrary);
#endif
            _initialized = true;
        }
    }

#if NET5_0_OR_GREATER
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "jxlrs")
        {
            return IntPtr.Zero;
        }

        // Return cached handle if already resolved
        if (_cachedHandle != IntPtr.Zero)
        {
            return _cachedHandle;
        }

        // Try standard resolution first
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
        {
            _cachedHandle = handle;
            return handle;
        }

        // Determine the platform-specific library name
        string libName = GetPlatformLibraryName();
        string rid = GetRuntimeIdentifier();
        
        // Get the assembly location
        string? assemblyLocation = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(assemblyLocation))
        {
            return IntPtr.Zero;
        }

        // Try loading from runtimes folder structure
        string runtimesPath = Path.Combine(assemblyLocation, "runtimes", rid, "native", libName);
        if (File.Exists(runtimesPath) && NativeLibrary.TryLoad(runtimesPath, out handle))
        {
            _cachedHandle = handle;
            return handle;
        }

        // Try loading directly from the assembly directory (fallback)
        string directPath = Path.Combine(assemblyLocation, libName);
        if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out handle))
        {
            _cachedHandle = handle;
            return handle;
        }

        return IntPtr.Zero;
    }

    private static string GetPlatformLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "jxlrs.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libjxlrs.dylib";
        return "libjxlrs.so";
    }

    private static string GetRuntimeIdentifier()
    {
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = "osx";
        else
            os = "linux";

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }
#endif
}
