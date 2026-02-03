namespace JpegXL.Net;

/// <summary>
/// Represents a metadata box from a JPEG XL file.
/// </summary>
/// <remarks>
/// Metadata boxes can be stored uncompressed or brotli-compressed (brob boxes) in the file.
/// When <see cref="IsBrotliCompressed"/> is true, the <see cref="Data"/> contains the raw
/// brotli-compressed bytes. The caller is responsible for decompression if needed.
/// </remarks>
public readonly struct JxlMetadataBox
{
    /// <summary>
    /// The raw data bytes of the metadata box.
    /// </summary>
    /// <remarks>
    /// If <see cref="IsBrotliCompressed"/> is true, this contains brotli-compressed data
    /// that must be decompressed to access the original metadata.
    /// </remarks>
    public byte[] Data { get; }

    /// <summary>
    /// Whether the metadata box was brotli-compressed in the file (brob box).
    /// </summary>
    /// <remarks>
    /// When true, the <see cref="Data"/> contains compressed bytes.
    /// The caller must decompress the data to access the original metadata content.
    /// </remarks>
    public bool IsBrotliCompressed { get; }

    /// <summary>
    /// Creates a new metadata box.
    /// </summary>
    /// <param name="data">The raw data bytes.</param>
    /// <param name="isBrotliCompressed">Whether the data is brotli-compressed.</param>
    public JxlMetadataBox(byte[] data, bool isBrotliCompressed)
    {
        Data = data;
        IsBrotliCompressed = isBrotliCompressed;
    }
}
