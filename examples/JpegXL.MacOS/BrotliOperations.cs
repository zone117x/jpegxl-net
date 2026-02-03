using System.IO.Compression;
using System.Runtime.InteropServices;

namespace JpegXL.MacOS;

/// <summary>
/// Brotli compression using .NET and decompression using macOS native Compression framework.
/// </summary>
public static class BrotliOperations
{
    // macOS Compression framework constant for Brotli algorithm
    private const uint COMPRESSION_BROTLI = 0x0B02;

    /// <summary>
    /// Compresses a file using .NET's BrotliStream.
    /// Output file will be the input path with .br extension appended.
    /// </summary>
    /// <param name="inputPath">Path to the file to compress</param>
    /// <returns>Path to the compressed output file</returns>
    public static string CompressWithDotNet(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        string outputPath = inputPath + ".br";
        var inputInfo = new FileInfo(inputPath);

        Console.WriteLine($"[Brotli] Compressing with .NET: {inputPath}");
        Console.WriteLine($"[Brotli] Original size: {inputInfo.Length:N0} bytes");

        using (var inputStream = File.OpenRead(inputPath))
        using (var outputStream = File.Create(outputPath))
        using (var brotliStream = new BrotliStream(outputStream, CompressionLevel.Optimal))
        {
            inputStream.CopyTo(brotliStream);
        }

        var outputInfo = new FileInfo(outputPath);
        Console.WriteLine($"[Brotli] Compressed size: {outputInfo.Length:N0} bytes");
        Console.WriteLine($"[Brotli] Output: {outputPath}");

        return outputPath;
    }

    /// <summary>
    /// Compresses a file using macOS native Compression framework.
    /// Output file will be the input path with .br extension appended.
    /// </summary>
    /// <param name="inputPath">Path to the file to compress</param>
    /// <returns>Path to the compressed output file</returns>
    public static string CompressWithMacOS(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        string outputPath = inputPath + ".br";

        byte[] inputData = File.ReadAllBytes(inputPath);
        Console.WriteLine($"[Brotli] Compressing with macOS: {inputPath}");
        Console.WriteLine($"[Brotli] Original size: {inputData.Length:N0} bytes");

        // Allocate output buffer - compressed size is at most input size + some overhead
        // For Brotli, worst case is slightly larger than input
        nint maxCompressedSize = inputData.Length + (inputData.Length / 10) + 1024;

        byte[] compressedData;
        nint compressedSize;

        unsafe
        {
            fixed (byte* srcPtr = inputData)
            {
                byte[] buffer = new byte[maxCompressedSize];
                fixed (byte* dstPtr = buffer)
                {
                    compressedSize = compression_encode_buffer(
                        (IntPtr)dstPtr,
                        maxCompressedSize,
                        (IntPtr)srcPtr,
                        inputData.Length,
                        IntPtr.Zero,
                        COMPRESSION_BROTLI);

                    if (compressedSize == 0)
                    {
                        throw new InvalidOperationException(
                            "Compression failed - macOS Compression framework returned 0");
                    }

                    // Copy to right-sized array
                    compressedData = new byte[compressedSize];
                    Array.Copy(buffer, compressedData, (int)compressedSize);
                }
            }
        }

        File.WriteAllBytes(outputPath, compressedData);

        Console.WriteLine($"[Brotli] Compressed size: {compressedSize:N0} bytes");
        Console.WriteLine($"[Brotli] Output: {outputPath}");

        return outputPath;
    }

    /// <summary>
    /// Decompresses a .br file using macOS native Compression framework.
    /// Output file will be the input path without the .br extension.
    /// </summary>
    /// <param name="inputPath">Path to the .br file to decompress</param>
    /// <returns>Path to the decompressed output file</returns>
    public static string DecompressWithMacOS(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        if (!inputPath.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Input file must have .br extension: {inputPath}");

        string outputPath = inputPath[..^3]; // Remove .br extension

        byte[] compressedData = File.ReadAllBytes(inputPath);
        Console.WriteLine($"[Brotli] Decompressing {inputPath}");
        Console.WriteLine($"[Brotli] Compressed size: {compressedData.Length:N0} bytes");

        // Allocate output buffer - Brotli typically achieves 3-6x compression, start with 10x
        nint estimatedSize = compressedData.Length * 10;
        if (estimatedSize < 1024)
            estimatedSize = 1024; // Minimum buffer size

        byte[] decompressedData;
        nint decompressedSize;

        unsafe
        {
            fixed (byte* srcPtr = compressedData)
            {
                while (true)
                {
                    byte[] buffer = new byte[estimatedSize];
                    fixed (byte* dstPtr = buffer)
                    {
                        decompressedSize = compression_decode_buffer(
                            (IntPtr)dstPtr,
                            estimatedSize,
                            (IntPtr)srcPtr,
                            compressedData.Length,
                            IntPtr.Zero,
                            COMPRESSION_BROTLI);

                        if (decompressedSize == 0)
                        {
                            throw new InvalidOperationException(
                                "Decompression failed - invalid Brotli data or corrupted file");
                        }

                        if (decompressedSize < estimatedSize)
                        {
                            // Success - copy to right-sized array
                            decompressedData = new byte[decompressedSize];
                            Array.Copy(buffer, decompressedData, (int)decompressedSize);
                            break;
                        }

                        // Buffer was exactly filled, might need more space
                        estimatedSize *= 2;
                        if (estimatedSize > int.MaxValue)
                        {
                            throw new InvalidOperationException(
                                "Decompressed data exceeds maximum supported size");
                        }
                    }
                }
            }
        }

        File.WriteAllBytes(outputPath, decompressedData);

        Console.WriteLine($"[Brotli] Decompressed size: {decompressedSize:N0} bytes");
        Console.WriteLine($"[Brotli] Output: {outputPath}");

        return outputPath;
    }

    // P/Invoke declarations for macOS Compression framework
    [DllImport("libcompression.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint compression_encode_buffer(
        IntPtr dst_buffer,
        nint dst_size,
        IntPtr src_buffer,
        nint src_size,
        IntPtr scratch_buffer,
        uint algorithm);

    [DllImport("libcompression.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint compression_decode_buffer(
        IntPtr dst_buffer,
        nint dst_size,
        IntPtr src_buffer,
        nint src_size,
        IntPtr scratch_buffer,
        uint algorithm);
}
