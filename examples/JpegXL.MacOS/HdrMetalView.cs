using System.Runtime.InteropServices;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Metal;

namespace JpegXL.MacOS;

/// <summary>
/// A view that renders images using Metal with EDR (Extended Dynamic Range) support.
/// </summary>
public class HdrMetalView : NSView
{
    private CAMetalLayer? _metalLayer;
    private IMTLDevice? _device;
    private IMTLCommandQueue? _commandQueue;
    private IMTLRenderPipelineState? _pipelineState;
    private IMTLRenderPipelineState? _arrayPipelineState;
    private IMTLTexture? _imageTexture;
    private IMTLBuffer? _vertexBuffer;

    // Animation texture array support
    private IMTLTexture? _animationTextureArray;
    private IMTLBuffer? _frameIndexBuffer;
    private int _currentArrayFrameIndex;

    private int _imageWidth;
    private int _imageHeight;
    private bool _isHdr;
    private float _hdrBrightnessScale = 1.0f;
    private nfloat _zoom = 1.0f;
    private CGPoint _offset = CGPoint.Empty;

    // Mouse drag tracking
    private bool _isDragging;
    private CGPoint _dragStartLocation;
    private CGPoint _dragStartOffset;

    public bool IsHdr => _isHdr;
    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;

    /// <summary>
    /// Optional scissor rect to clip rendering. When set, only the specified
    /// portion of the drawable is rendered. Coordinates are in pixels (not points).
    /// </summary>
    public MTLScissorRect? ScissorRect { get; set; }

    /// <summary>
    /// When true, this view does not handle mouse/scroll events and is invisible to hit testing.
    /// Used for the SDR overlay in comparison mode.
    /// </summary>
    public bool PassThroughEvents { get; set; }

    /// <summary>
    /// Called whenever zoom or offset changes from user interaction.
    /// Used to sync another view's viewport in comparison mode.
    /// </summary>
    public Action<nfloat, CGPoint>? OnViewportChanged { get; set; }

    /// <summary>
    /// Sets the HDR brightness scale for EDR display.
    /// For HDR content, this should be IntensityTarget / SDR_REFERENCE_WHITE (typically 203 nits).
    /// Values > 1.0 will use EDR headroom for brighter-than-SDR-white display.
    /// Note: When using HLG mode with CAEdrMetadata, this is ignored as the system handles tone mapping.
    /// </summary>
    public float HdrBrightnessScale
    {
        get => _hdrBrightnessScale;
        set
        {
            _hdrBrightnessScale = value;
            Render();
        }
    }

    /// <summary>
    /// Configures the Metal layer for HLG HDR content.
    /// Uses system tone mapping via CAEdrMetadata for correct black levels and HDR highlights.
    /// </summary>
    public void ConfigureForHlg()
    {
        if (_metalLayer == null) return;

        // Use HLG color space - content stays in HLG encoding
        var hlgColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.Itur_2100_Hlg);
        _metalLayer.ColorSpace = hlgColorSpace;

        // Enable system tone mapping for HLG
        _metalLayer.EdrMetadata = CAEdrMetadata.HlgMetadata;

        // Brightness scale not needed - system handles tone mapping
        _hdrBrightnessScale = 1.0f;

        Console.WriteLine("[HdrMetalView] Configured for HLG with system tone mapping");
    }

    /// <summary>
    /// Configures the Metal layer for PQ (HDR10) content.
    /// Uses system tone mapping via CAEdrMetadata for correct display.
    /// </summary>
    /// <param name="maxLuminance">Maximum content luminance in nits (e.g., 1000 for typical HDR).</param>
    public void ConfigureForPq(float maxLuminance = 1000f)
    {
        if (_metalLayer == null) return;

        // Use PQ color space - content stays in PQ encoding
        var pqColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.Itur_2100_PQ);
        _metalLayer.ColorSpace = pqColorSpace;

        // Enable system tone mapping for PQ/HDR10
        // opticalOutputScale is SDR reference white in nits (typically 100)
        _metalLayer.EdrMetadata = CAEdrMetadata.GetHdr10Metadata(0f, maxLuminance, 100f);

        // Brightness scale not needed - system handles tone mapping
        _hdrBrightnessScale = 1.0f;

        Console.WriteLine($"[HdrMetalView] Configured for PQ with system tone mapping (max: {maxLuminance} nits)");
    }

    /// <summary>
    /// Configures the Metal layer for SDR sRGB content.
    /// Uses extended sRGB color space for standard gamma-encoded content.
    /// </summary>
    public void ConfigureForSrgb()
    {
        if (_metalLayer == null) return;

        // Use extended sRGB for SDR content (supports values outside 0-1 range)
        var srgbColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.ExtendedSrgb);
        _metalLayer.ColorSpace = srgbColorSpace;

        // No EDR metadata for SDR content
        _metalLayer.EdrMetadata = null;

        Console.WriteLine("[HdrMetalView] Configured for sRGB color space");
    }

    /// <summary>
    /// Configures the Metal layer for HDR content with manual brightness control.
    /// Uses extended linear Display P3 for flexibility.
    /// </summary>
    public void ConfigureForLinear()
    {
        if (_metalLayer == null) return;

        // Use extended linear P3 color space
        var linearP3ColorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.ExtendedLinearDisplayP3);
        _metalLayer.ColorSpace = linearP3ColorSpace;

        // Disable system tone mapping - we'll handle brightness manually if needed
        _metalLayer.EdrMetadata = null;

        Console.WriteLine("[HdrMetalView] Configured for linear color space");
    }

    /// <summary>
    /// Called when zoom level changes (from scroll wheel, buttons, etc.)
    /// </summary>
    public Action<nfloat>? OnZoomChanged { get; set; }

    public nfloat Zoom
    {
        get => _zoom;
        set
        {
            _zoom = (nfloat)Math.Clamp((double)value, 0.1, 100.0);
            ClampOffset();
            Render();
            OnZoomChanged?.Invoke(_zoom);
            OnViewportChanged?.Invoke(_zoom, _offset);
        }
    }

    public CGPoint Offset
    {
        get => _offset;
        set
        {
            _offset = value;
            ClampOffset();
            Render();
        }
    }

    /// <summary>
    /// Resets zoom to 1:1 pixel ratio and centers the image.
    /// </summary>
    public void ResetView()
    {
        _zoom = 1.0f;
        _offset = CGPoint.Empty;
        Render();
        OnZoomChanged?.Invoke(_zoom);
        OnViewportChanged?.Invoke(_zoom, _offset);
    }

    /// <summary>
    /// Zooms to fit the entire image in the view.
    /// </summary>
    public void ZoomToFit()
    {
        if (_imageWidth == 0 || _imageHeight == 0) return;

        var contentsScale = _metalLayer?.ContentsScale ?? (nfloat)2.0;
        var viewWidthPixels = Bounds.Width * contentsScale;
        var viewHeightPixels = Bounds.Height * contentsScale;

        // Calculate zoom level that fits the entire image
        var zoomX = viewWidthPixels / _imageWidth;
        var zoomY = viewHeightPixels / _imageHeight;
        _zoom = (nfloat)Math.Min((double)zoomX, (double)zoomY);
        _offset = CGPoint.Empty;
        Render();
        OnZoomChanged?.Invoke(_zoom);
        OnViewportChanged?.Invoke(_zoom, _offset);
    }

    /// <summary>
    /// Sets zoom and offset without triggering OnViewportChanged callback.
    /// Used for syncing from another view to prevent infinite callback loops.
    /// </summary>
    public void SetViewportSilently(nfloat zoom, CGPoint offset)
    {
        _zoom = (nfloat)Math.Clamp((double)zoom, 0.1, 100.0);
        _offset = offset;
        ClampOffset();
        Render();
    }

    public override bool AcceptsFirstResponder() => true;
    public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

    public override NSView? HitTest(CGPoint point)
    {
        return PassThroughEvents ? null : base.HitTest(point);
    }

    public HdrMetalView(CGRect frame) : base(frame)
    {
        Initialize();
    }

    public HdrMetalView(NSCoder coder) : base(coder)
    {
        Initialize();
    }

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        Render();
        OnViewportChanged?.Invoke(_zoom, _offset);
    }

    private void Initialize()
    {
        // Initialize Metal device BEFORE setting WantsLayer, because
        // WantsLayer = true triggers MakeBackingLayer() which needs _device
        _device = MTLDevice.SystemDefault;
        if (_device == null)
        {
            throw new InvalidOperationException("Metal is not supported on this device");
        }

        _commandQueue = _device.CreateCommandQueue();
        CreatePipelineState();
        CreateVertexBuffer();

        // Now enable layer-backing - this calls MakeBackingLayer()
        WantsLayer = true;

        // Register for drag and drop
        RegisterForDraggedTypes([NSPasteboardType.FileUrl.GetConstant()!]);
    }

    public override CALayer MakeBackingLayer()
    {
        _metalLayer = new CAMetalLayer
        {
            Device = _device,
            PixelFormat = MTLPixelFormat.RGBA16Float,
            FramebufferOnly = false,
            Opaque = false,
            // Use window's screen if available, fall back to main screen
            ContentsScale = Window?.Screen?.BackingScaleFactor
                ?? NSScreen.MainScreen?.BackingScaleFactor
                ?? (nfloat)2.0
        };

        // Enable EDR for HDR content
        _metalLayer.WantsExtendedDynamicRangeContent = true;

        // Use extended linear Display P3 color space for HDR
        var colorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.ExtendedLinearDisplayP3);
        _metalLayer.ColorSpace = colorSpace;

        return _metalLayer;
    }

    /// <summary>
    /// Updates the Metal layer's ContentsScale to match the current screen's backing scale factor.
    /// Call this when the window moves to a different screen.
    /// </summary>
    public void UpdateContentsScale()
    {
        var newScale = Window?.Screen?.BackingScaleFactor
            ?? NSScreen.MainScreen?.BackingScaleFactor
            ?? (nfloat)2.0;

        if (_metalLayer != null && _metalLayer.ContentsScale != newScale)
        {
            _metalLayer.ContentsScale = newScale;
            NeedsDisplay = true;
        }
    }

    private void CreatePipelineState()
    {
        if (_device == null) return;

        // Metal Shading Language source for rendering a textured quad
        var shaderSource = @"
#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float2 position [[attribute(0)]];
    float2 texCoord [[attribute(1)]];
};

struct VertexOut {
    float4 position [[position]];
    float2 texCoord;
};

struct VertexUniforms {
    float2 scale;
    float2 offset;
};

struct FragmentUniforms {
    float brightnessScale;  // HDR brightness multiplier (1.0 for SDR, >1.0 for HDR)
};

vertex VertexOut vertexShader(VertexIn in [[stage_in]], constant VertexUniforms& uniforms [[buffer(1)]]) {
    VertexOut out;
    out.position = float4(in.position * uniforms.scale + uniforms.offset, 0.0, 1.0);
    out.texCoord = in.texCoord;
    return out;
}

fragment float4 fragmentShader(VertexOut in [[stage_in]],
                               texture2d<float> tex [[texture(0)]],
                               constant FragmentUniforms& uniforms [[buffer(1)]]) {
    constexpr sampler s(mag_filter::linear, min_filter::linear);
    float4 color = tex.sample(s, in.texCoord);
    // Scale by brightness for EDR display
    color.rgb *= uniforms.brightnessScale;
    return color;
}

// Fragment shader for texture array (animation frames)
fragment float4 fragmentShaderArray(VertexOut in [[stage_in]],
                                     texture2d_array<float> tex [[texture(0)]],
                                     constant int& frameIndex [[buffer(0)]],
                                     constant FragmentUniforms& uniforms [[buffer(1)]]) {
    constexpr sampler s(mag_filter::linear, min_filter::linear);
    float4 color = tex.sample(s, in.texCoord, frameIndex);
    // Scale by brightness for EDR display
    color.rgb *= uniforms.brightnessScale;
    return color;
}
";

        NSError? error;
        using var compileOptions = new MTLCompileOptions();
        using var library = _device.CreateLibrary(shaderSource, compileOptions, out error);
        if (library == null || error != null)
        {
            Console.WriteLine($"Failed to create shader library: {error?.LocalizedDescription}");
            return;
        }

        using var vertexFunction = library.CreateFunction("vertexShader");
        using var fragmentFunction = library.CreateFunction("fragmentShader");

        using var vertexDescriptor = new MTLVertexDescriptor();
        // Position
        vertexDescriptor.Attributes[0].Format = MTLVertexFormat.Float2;
        vertexDescriptor.Attributes[0].Offset = 0;
        vertexDescriptor.Attributes[0].BufferIndex = 0;
        // TexCoord
        vertexDescriptor.Attributes[1].Format = MTLVertexFormat.Float2;
        vertexDescriptor.Attributes[1].Offset = 8;
        vertexDescriptor.Attributes[1].BufferIndex = 0;
        // Layout
        vertexDescriptor.Layouts[0].Stride = 16;
        vertexDescriptor.Layouts[0].StepFunction = MTLVertexStepFunction.PerVertex;

        using var pipelineDescriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertexFunction,
            FragmentFunction = fragmentFunction,
            VertexDescriptor = vertexDescriptor
        };
        pipelineDescriptor.ColorAttachments[0].PixelFormat = MTLPixelFormat.RGBA16Float;
        // Enable alpha blending for transparency (premultiplied alpha)
        pipelineDescriptor.ColorAttachments[0].BlendingEnabled = true;
        pipelineDescriptor.ColorAttachments[0].SourceRgbBlendFactor = MTLBlendFactor.One;
        pipelineDescriptor.ColorAttachments[0].DestinationRgbBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
        pipelineDescriptor.ColorAttachments[0].SourceAlphaBlendFactor = MTLBlendFactor.One;
        pipelineDescriptor.ColorAttachments[0].DestinationAlphaBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;

        _pipelineState = _device.CreateRenderPipelineState(pipelineDescriptor, out error);
        if (_pipelineState == null || error != null)
        {
            Console.WriteLine($"Failed to create pipeline state: {error?.LocalizedDescription}");
        }

        // Create pipeline state for texture array (animation)
        using var fragmentArrayFunction = library.CreateFunction("fragmentShaderArray");
        using var arrayPipelineDescriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertexFunction,
            FragmentFunction = fragmentArrayFunction,
            VertexDescriptor = vertexDescriptor
        };
        arrayPipelineDescriptor.ColorAttachments[0].PixelFormat = MTLPixelFormat.RGBA16Float;
        // Enable alpha blending for transparency (premultiplied alpha)
        arrayPipelineDescriptor.ColorAttachments[0].BlendingEnabled = true;
        arrayPipelineDescriptor.ColorAttachments[0].SourceRgbBlendFactor = MTLBlendFactor.One;
        arrayPipelineDescriptor.ColorAttachments[0].DestinationRgbBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
        arrayPipelineDescriptor.ColorAttachments[0].SourceAlphaBlendFactor = MTLBlendFactor.One;
        arrayPipelineDescriptor.ColorAttachments[0].DestinationAlphaBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;

        _arrayPipelineState = _device.CreateRenderPipelineState(arrayPipelineDescriptor, out error);
        if (_arrayPipelineState == null || error != null)
        {
            Console.WriteLine($"Failed to create array pipeline state: {error?.LocalizedDescription}");
        }
    }

    private void CreateVertexBuffer()
    {
        if (_device == null) return;

        // Quad vertices: position (x, y), texCoord (u, v)
        float[] vertices =
        [
            -1.0f, -1.0f, 0.0f, 1.0f, // Bottom-left
             1.0f, -1.0f, 1.0f, 1.0f, // Bottom-right
            -1.0f,  1.0f, 0.0f, 0.0f, // Top-left
             1.0f,  1.0f, 1.0f, 0.0f, // Top-right
        ];

        var handle = GCHandle.Alloc(vertices, GCHandleType.Pinned);
        try
        {
            _vertexBuffer = _device.CreateBuffer(
                handle.AddrOfPinnedObject(),
                (nuint)(vertices.Length * sizeof(float)),
                MTLResourceOptions.StorageModeShared);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Decodes an image directly into GPU-shared memory, eliminating managed array allocation.
    /// On Apple Silicon, this uses unified memory accessible by both CPU and GPU.
    /// </summary>
    /// <param name="width">Width of the image in pixels.</param>
    /// <param name="height">Height of the image in pixels.</param>
    /// <param name="decodeAction">Action that receives a Span to decode pixels into.</param>
    public void DecodeDirectToGpu(int width, int height, Action<Span<float>> decodeAction)
    {
        if (_device == null) return;

        _imageWidth = width;
        _imageHeight = height;
        _isHdr = true;

        // Clear animation state
        _animationTextureArray?.Dispose();
        _animationTextureArray = null;

        var pixelCount = width * height * 4;
        var bufferSize = (nuint)(pixelCount * sizeof(float));

        // Create shared buffer - on Apple Silicon this is unified memory
        using var sharedBuffer = _device.CreateBuffer(bufferSize, MTLResourceOptions.StorageModeShared);
        if (sharedBuffer == null) return;

        // Get CPU pointer and wrap in Span for decoding
        var cpuPtr = sharedBuffer.Contents;
        Span<float> pixelSpan;
        unsafe
        {
            pixelSpan = new Span<float>((void*)cpuPtr, pixelCount);
        }

        // Decode directly into GPU-shared memory
        decodeAction(pixelSpan);

        // Create texture from the shared buffer
        // On Apple Silicon, this is very fast as it's already in unified memory
        using var textureDescriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(
            MTLPixelFormat.RGBA32Float,
            (nuint)width,
            (nuint)height,
            false);
        textureDescriptor.Usage = MTLTextureUsage.ShaderRead;
        textureDescriptor.StorageMode = MTLStorageMode.Shared;

        _imageTexture?.Dispose();
        _imageTexture = _device.CreateTexture(textureDescriptor);

        // Copy from shared buffer to texture
        // On unified memory architecture, this is essentially a metadata operation
        _imageTexture?.ReplaceRegion(
            new MTLRegion(new MTLOrigin(0, 0, 0), new MTLSize(width, height, 1)),
            0,
            cpuPtr,
            (nuint)(width * 4 * sizeof(float)));

        Render();
    }

    // Shared buffer for sequential frame decoding
    private IMTLBuffer? _decodingBuffer;
    private nint _decodingBufferPtr;
    private int _decodingPixelCount;

    /// <summary>
    /// Prepares GPU resources for animation frame decoding.
    /// Call this before DecodeFrameToGpu() for each frame.
    /// </summary>
    public void PrepareAnimationTextures(int frameCount, int width, int height)
    {
        if (_device == null || frameCount == 0) return;

        _imageWidth = width;
        _imageHeight = height;
        _isHdr = true;

        // Clear single-image state
        _imageTexture?.Dispose();
        _imageTexture = null;

        // Create texture array descriptor
        using var descriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(
            MTLPixelFormat.RGBA32Float,
            (nuint)width,
            (nuint)height,
            false);
        descriptor.TextureType = MTLTextureType.k2DArray;
        descriptor.ArrayLength = (nuint)frameCount;
        descriptor.Usage = MTLTextureUsage.ShaderRead;
        descriptor.StorageMode = MTLStorageMode.Shared;

        _animationTextureArray?.Dispose();
        _animationTextureArray = _device.CreateTexture(descriptor);

        // Create shared buffer for decoding (reused for each frame)
        _decodingPixelCount = width * height * 4;
        var bufferSize = (nuint)(_decodingPixelCount * sizeof(float));
        _decodingBuffer?.Dispose();
        _decodingBuffer = _device.CreateBuffer(bufferSize, MTLResourceOptions.StorageModeShared);
        _decodingBufferPtr = _decodingBuffer?.Contents ?? nint.Zero;

        // Create buffer for frame index uniform
        _frameIndexBuffer?.Dispose();
        _frameIndexBuffer = _device.CreateBuffer((nuint)sizeof(int), MTLResourceOptions.StorageModeShared);
    }

    /// <summary>
    /// Decodes a single frame directly into GPU-shared memory.
    /// Call PrepareAnimationTextures() first, then call this for each frame in sequence.
    /// </summary>
    public void DecodeFrameToGpu(int frameIndex, Action<Span<float>> decodeAction)
    {
        if (_animationTextureArray == null || _decodingBuffer == null || _decodingBufferPtr == nint.Zero)
            return;

        // Create span pointing to shared buffer
        Span<float> pixelSpan;
        unsafe
        {
            pixelSpan = new Span<float>((void*)_decodingBufferPtr, _decodingPixelCount);
        }

        // Decode frame into shared buffer
        decodeAction(pixelSpan);

        // Upload to texture array slice
        var bytesPerRow = (nuint)(_imageWidth * 4 * sizeof(float));
        var bytesPerImage = (nuint)(_imageWidth * _imageHeight * 4 * sizeof(float));
        _animationTextureArray.ReplaceRegion(
            new MTLRegion(new MTLOrigin(0, 0, 0), new MTLSize(_imageWidth, _imageHeight, 1)),
            0,
            (nuint)frameIndex,
            _decodingBufferPtr,
            bytesPerRow,
            bytesPerImage);
    }

    /// <summary>
    /// Finishes animation setup and displays the first frame.
    /// Call after all frames have been decoded with DecodeFrameToGpu().
    /// </summary>
    public void FinishAnimationSetup()
    {
        // Dispose decoding buffer - no longer needed
        _decodingBuffer?.Dispose();
        _decodingBuffer = null;
        _decodingBufferPtr = nint.Zero;
        _decodingPixelCount = 0;

        // Display first frame
        DisplayArrayFrame(0);
    }

    /// <summary>
    /// Displays a specific frame from the animation texture array.
    /// </summary>
    /// <param name="index">The frame index to display.</param>
    public void DisplayArrayFrame(int index)
    {
        if (_animationTextureArray == null || _frameIndexBuffer == null) return;

        // Track current frame for readback
        _currentArrayFrameIndex = index;

        // Update frame index in buffer
        Marshal.WriteInt32(_frameIndexBuffer.Contents, index);

        Render();
    }

    /// <summary>
    /// Reads current frame pixels from GPU texture and converts to SDR RGBA8.
    /// Used for export to PNG/JPEG.
    /// </summary>
    public byte[]? ReadbackPixelsAsSdr()
    {
        var texture = _animationTextureArray ?? _imageTexture;
        if (texture == null || _imageWidth == 0 || _imageHeight == 0) return null;

        var width = _imageWidth;
        var height = _imageHeight;
        var floatBytesPerRow = width * 4 * sizeof(float);  // RGBA32Float

        // Allocate managed array for float pixels
        var floatPixels = new float[width * height * 4];

        // Copy from GPU texture to CPU
        var region = new MTLRegion(new MTLOrigin(0, 0, 0), new MTLSize(width, height, 1));

        // Pin the array and get bytes
        var handle = GCHandle.Alloc(floatPixels, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();

            if (_animationTextureArray != null)
            {
                // Read current frame from texture array
                texture.GetBytes(ptr, (nuint)floatBytesPerRow, (nuint)0, region,
                    (nuint)_currentArrayFrameIndex, 0);
            }
            else
            {
                texture.GetBytes(ptr, (nuint)floatBytesPerRow, region, 0);
            }
        }
        finally
        {
            handle.Free();
        }

        // Convert RGBA32Float (HDR, linear P3) to RGBA8 (SDR, sRGB)
        var sdrPixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            var r = floatPixels[i * 4 + 0];
            var g = floatPixels[i * 4 + 1];
            var b = floatPixels[i * 4 + 2];
            var a = floatPixels[i * 4 + 3];

            // Un-premultiply alpha
            if (a > 0.001f)
            {
                r /= a;
                g /= a;
                b /= a;
            }

            // Simple tone mapping: clamp HDR to [0,1]
            r = Math.Clamp(r, 0f, 1f);
            g = Math.Clamp(g, 0f, 1f);
            b = Math.Clamp(b, 0f, 1f);

            // Linear to sRGB gamma
            r = LinearToSrgb(r);
            g = LinearToSrgb(g);
            b = LinearToSrgb(b);

            // Convert to 8-bit
            sdrPixels[i * 4 + 0] = (byte)(r * 255f + 0.5f);
            sdrPixels[i * 4 + 1] = (byte)(g * 255f + 0.5f);
            sdrPixels[i * 4 + 2] = (byte)(b * 255f + 0.5f);
            sdrPixels[i * 4 + 3] = (byte)(a * 255f + 0.5f);
        }

        return sdrPixels;
    }

    private static float LinearToSrgb(float linear)
    {
        if (linear <= 0.0031308f)
            return linear * 12.92f;
        return 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    /// <summary>
    /// Renders the current image to the Metal layer.
    /// </summary>
    public void Render()
    {
        // Check if we have either a single texture or an animation texture array
        var useArrayTexture = _animationTextureArray != null && _arrayPipelineState != null && _frameIndexBuffer != null;
        var useSingleTexture = _imageTexture != null && _pipelineState != null;

        if (_metalLayer == null || _device == null || _commandQueue == null ||
            _vertexBuffer == null || (!useArrayTexture && !useSingleTexture))
        {
            return;
        }

        // Update drawable size
        _metalLayer.DrawableSize = new CGSize(
            Bounds.Width * (_metalLayer.ContentsScale),
            Bounds.Height * (_metalLayer.ContentsScale));

        using var drawable = _metalLayer.NextDrawable();
        if (drawable == null) return;

        using var commandBuffer = _commandQueue.CommandBuffer();
        if (commandBuffer == null) return;

        using var passDescriptor = new MTLRenderPassDescriptor();
        passDescriptor.ColorAttachments[0].Texture = drawable.Texture;
        passDescriptor.ColorAttachments[0].LoadAction = MTLLoadAction.Clear;
        passDescriptor.ColorAttachments[0].StoreAction = MTLStoreAction.Store;

        // Use transparent clear color - the window background shows through
        passDescriptor.ColorAttachments[0].ClearColor = new MTLClearColor(0, 0, 0, 0);

        using var encoder = commandBuffer.CreateRenderCommandEncoder(passDescriptor);
        if (encoder == null) return;

        // Use array pipeline for animations, single texture pipeline for static images
        if (useArrayTexture)
        {
            encoder.SetRenderPipelineState(_arrayPipelineState!);
            encoder.SetFragmentTexture(_animationTextureArray!, 0);
            encoder.SetFragmentBuffer(_frameIndexBuffer!, 0, 0);
        }
        else
        {
            encoder.SetRenderPipelineState(_pipelineState!);
            encoder.SetFragmentTexture(_imageTexture!, 0);
        }

        encoder.SetVertexBuffer(_vertexBuffer, 0, 0);

        // Apply scissor rect if set (for comparison mode clipping)
        if (ScissorRect.HasValue)
        {
            encoder.SetScissorRect(ScissorRect.Value);
        }

        // Calculate scale for 1:1 pixel ratio at zoom=1.0
        // The quad spans -1 to 1 in NDC, which maps to the full drawable
        // At zoom=1.0, we want 1 image pixel = 1 screen pixel
        var contentsScale = _metalLayer.ContentsScale;
        var viewWidthPixels = Bounds.Width * contentsScale;
        var viewHeightPixels = Bounds.Height * contentsScale;

        // Scale factors: how much of the NDC space the image occupies
        // At zoom=1.0, imageWidth pixels should take imageWidth/viewWidth of the view
        var scaleX = (float)((nfloat)_imageWidth / viewWidthPixels * _zoom);
        var scaleY = (float)((nfloat)_imageHeight / viewHeightPixels * _zoom);

        // Vertex uniforms: scale and offset
        float[] vertexUniforms = [scaleX, scaleY, (float)_offset.X, (float)_offset.Y];
        var vertexUniformHandle = GCHandle.Alloc(vertexUniforms, GCHandleType.Pinned);
        try
        {
            encoder.SetVertexBytes(vertexUniformHandle.AddrOfPinnedObject(), (nuint)(vertexUniforms.Length * sizeof(float)), 1);
        }
        finally
        {
            vertexUniformHandle.Free();
        }

        // Fragment uniforms: HDR brightness scale
        float[] fragmentUniforms = [_hdrBrightnessScale];
        var fragmentUniformHandle = GCHandle.Alloc(fragmentUniforms, GCHandleType.Pinned);
        try
        {
            encoder.SetFragmentBytes(fragmentUniformHandle.AddrOfPinnedObject(), (nuint)(fragmentUniforms.Length * sizeof(float)), 1);
        }
        finally
        {
            fragmentUniformHandle.Free();
        }

        encoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4);
        encoder.EndEncoding();

        commandBuffer.PresentDrawable(drawable);
        commandBuffer.Commit();
    }

    // Drag and drop support
    public override NSDragOperation DraggingEntered(INSDraggingInfo sender)
    {
        var dominated = sender.DraggingPasteboard.Types?.Contains(NSPasteboardType.FileUrl.GetConstant()) ?? false;
        return dominated ? NSDragOperation.Copy : NSDragOperation.None;
    }

    public override bool PerformDragOperation(INSDraggingInfo sender)
    {
        var urls = sender.DraggingPasteboard.ReadObjectsForClasses(
            new[] { new ObjCRuntime.Class(typeof(NSUrl)) },
            null);

        if (urls?.FirstOrDefault() is NSUrl url && url.Path != null)
        {
            if (url.Path.EndsWith(".jxl", StringComparison.OrdinalIgnoreCase))
            {
                // Notify window to load the file
                if (Window is MainWindow mainWindow)
                {
                    mainWindow.LoadImage(url.Path);
                    return true;
                }
            }
        }

        return false;
    }

    // Scroll wheel zoom support
    public override void ScrollWheel(NSEvent theEvent)
    {
        if (_imageWidth == 0 || _imageHeight == 0) return;

        // Get scroll delta (use scrollingDeltaY for trackpad/mouse wheel)
        var delta = theEvent.ScrollingDeltaY;
        if (Math.Abs(delta) < 0.01) return;

        // Calculate zoom factor (positive delta = zoom in)
        // Use a smaller multiplier for smoother zooming
        var zoomFactor = 1.0 + delta * 0.005;

        // Get mouse position in view coordinates
        var mouseLocation = ConvertPointFromView(theEvent.LocationInWindow, null);

        // Calculate mouse position relative to view center (in normalized coords -1 to 1)
        var viewCenterX = Bounds.Width / 2;
        var viewCenterY = Bounds.Height / 2;
        var mouseNormX = (mouseLocation.X - viewCenterX) / viewCenterX;
        var mouseNormY = (mouseLocation.Y - viewCenterY) / viewCenterY;

        // Store old zoom
        var oldZoom = _zoom;

        // Apply zoom
        _zoom = (nfloat)Math.Clamp((double)_zoom * zoomFactor, 0.1, 100.0);

        // Adjust offset to zoom toward mouse cursor
        // The offset is in normalized device coordinates (-1 to 1)
        var zoomRatio = _zoom / oldZoom;
        _offset = new CGPoint(
            _offset.X * zoomRatio + mouseNormX * (1 - zoomRatio),
            _offset.Y * zoomRatio + mouseNormY * (1 - zoomRatio)
        );

        ClampOffset();
        Render();
        OnZoomChanged?.Invoke(_zoom);
        OnViewportChanged?.Invoke(_zoom, _offset);
    }

    // Mouse drag panning support
    public override void MouseDown(NSEvent theEvent)
    {
        if (_imageWidth == 0 || _imageHeight == 0)
        {
            base.MouseDown(theEvent);
            return;
        }

        _isDragging = true;
        _dragStartLocation = ConvertPointFromView(theEvent.LocationInWindow, null);
        _dragStartOffset = _offset;
        NSCursor.ClosedHandCursor.Set();
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (!_isDragging)
        {
            base.MouseDragged(theEvent);
            return;
        }

        var currentLocation = ConvertPointFromView(theEvent.LocationInWindow, null);
        var deltaX = currentLocation.X - _dragStartLocation.X;
        var deltaY = currentLocation.Y - _dragStartLocation.Y;

        // Convert delta from view points to normalized device coordinates
        // The view spans 2 units in NDC (-1 to 1), so divide by half the view size
        var ndcDeltaX = deltaX / (Bounds.Width / 2);
        var ndcDeltaY = deltaY / (Bounds.Height / 2);

        _offset = new CGPoint(
            _dragStartOffset.X + ndcDeltaX,
            _dragStartOffset.Y + ndcDeltaY
        );

        ClampOffset();
        Render();
        OnViewportChanged?.Invoke(_zoom, _offset);
    }

    public override void MouseUp(NSEvent theEvent)
    {
        _isDragging = false;
        NSCursor.ArrowCursor.Set();
        base.MouseUp(theEvent);
    }

    /// <summary>
    /// Clamps the offset to prevent panning the image completely off-screen.
    /// </summary>
    private void ClampOffset()
    {
        if (_imageWidth == 0 || _imageHeight == 0 || _metalLayer == null) return;

        var contentsScale = _metalLayer.ContentsScale;
        var viewWidthPixels = Bounds.Width * contentsScale;
        var viewHeightPixels = Bounds.Height * contentsScale;

        // Calculate the scaled image size in NDC units
        // At zoom=1.0, the image takes (imageWidth/viewWidth) of the NDC space
        var imageScaleX = (nfloat)_imageWidth / viewWidthPixels * _zoom;
        var imageScaleY = (nfloat)_imageHeight / viewHeightPixels * _zoom;

        // Maximum offset allows the image edge to reach the opposite view edge
        // If image is smaller than view, don't allow offset
        var maxOffsetX = Math.Max(0, (double)(imageScaleX - 1));
        var maxOffsetY = Math.Max(0, (double)(imageScaleY - 1));

        _offset = new CGPoint(
            Math.Clamp((double)_offset.X, -maxOffsetX, maxOffsetX),
            Math.Clamp((double)_offset.Y, -maxOffsetY, maxOffsetY)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageTexture?.Dispose();
            _vertexBuffer?.Dispose();
            _pipelineState?.Dispose();
            _commandQueue?.Dispose();

            // Dispose animation resources
            _animationTextureArray?.Dispose();
            _frameIndexBuffer?.Dispose();
            _arrayPipelineState?.Dispose();
            _decodingBuffer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
