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
    private int _animationFrameCount;
    private int _currentArrayFrameIndex;

    private int _imageWidth;
    private int _imageHeight;
    private bool _isHdr;
    private nfloat _zoom = 1.0f;
    private CGPoint _offset = CGPoint.Empty;

    public bool IsHdr => _isHdr;
    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;

    public nfloat Zoom
    {
        get => _zoom;
        set
        {
            _zoom = (nfloat)Math.Clamp((double)value, 0.1, 10.0);
            Render();
        }
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
            ContentsScale = NSScreen.MainScreen?.BackingScaleFactor ?? (nfloat)2.0
        };

        // Enable EDR for HDR content
        _metalLayer.WantsExtendedDynamicRangeContent = true;

        // Use extended linear Display P3 color space for HDR
        var colorSpace = CGColorSpace.CreateWithName(CGColorSpaceNames.ExtendedLinearDisplayP3);
        _metalLayer.ColorSpace = colorSpace;

        return _metalLayer;
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

struct Uniforms {
    float2 scale;
    float2 offset;
};

vertex VertexOut vertexShader(VertexIn in [[stage_in]], constant Uniforms& uniforms [[buffer(1)]]) {
    VertexOut out;
    out.position = float4(in.position * uniforms.scale + uniforms.offset, 0.0, 1.0);
    out.texCoord = in.texCoord;
    return out;
}

fragment float4 fragmentShader(VertexOut in [[stage_in]], texture2d<float> tex [[texture(0)]]) {
    constexpr sampler s(mag_filter::linear, min_filter::linear);
    float4 color = tex.sample(s, in.texCoord);
    // RGB is already premultiplied at decode time, just output with opaque alpha
    return float4(color.rgb, 1.0);
}

// Fragment shader for texture array (animation frames)
fragment float4 fragmentShaderArray(VertexOut in [[stage_in]],
                                     texture2d_array<float> tex [[texture(0)]],
                                     constant int& frameIndex [[buffer(0)]]) {
    constexpr sampler s(mag_filter::linear, min_filter::linear);
    float4 color = tex.sample(s, in.texCoord, frameIndex);
    return float4(color.rgb, 1.0);
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
    /// Sets the image to display from BGRA8 pixel data (SDR).
    /// </summary>
    public void SetImageSdr(byte[] pixels, int width, int height)
    {
        _imageWidth = width;
        _imageHeight = height;
        _isHdr = false;

        CreateTextureFromBgra8(pixels, width, height);
        Render();
    }

    /// <summary>
    /// Sets the image to display from Float32 RGBA pixel data (HDR).
    /// </summary>
    public void SetImageHdr(float[] pixels, int width, int height)
    {
        _imageWidth = width;
        _imageHeight = height;
        _isHdr = true;

        // Clear animation state when setting a single image
        _animationTextureArray?.Dispose();
        _animationTextureArray = null;
        _animationFrameCount = 0;

        CreateTextureFromFloat32(pixels, width, height);
        Render();
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
        _animationFrameCount = 0;

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

    /// <summary>
    /// Uploads all animation frames to a GPU texture array for efficient playback.
    /// </summary>
    /// <param name="frames">List of float[] pixel data for each frame.</param>
    /// <param name="width">Width of each frame in pixels.</param>
    /// <param name="height">Height of each frame in pixels.</param>
    public void SetAnimationFrames(IReadOnlyList<float[]> frames, int width, int height)
    {
        if (_device == null || frames.Count == 0) return;

        _imageWidth = width;
        _imageHeight = height;
        _isHdr = true;
        _animationFrameCount = frames.Count;
        _currentArrayFrameIndex = 0;

        // Create texture array descriptor
        using var descriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(
            MTLPixelFormat.RGBA32Float,
            (nuint)width,
            (nuint)height,
            false);
        descriptor.TextureType = MTLTextureType.k2DArray;
        descriptor.ArrayLength = (nuint)frames.Count;
        descriptor.Usage = MTLTextureUsage.ShaderRead;
        descriptor.StorageMode = MTLStorageMode.Shared;

        _animationTextureArray?.Dispose();
        _animationTextureArray = _device.CreateTexture(descriptor);

        // Upload all frames to the texture array
        var bytesPerRow = (nuint)(width * 4 * sizeof(float));
        var bytesPerImage = (nuint)(width * height * 4 * sizeof(float));
        for (int i = 0; i < frames.Count; i++)
        {
            var handle = GCHandle.Alloc(frames[i], GCHandleType.Pinned);
            try
            {
                _animationTextureArray?.ReplaceRegion(
                    new MTLRegion(new MTLOrigin(0, 0, 0), new MTLSize(width, height, 1)),
                    0,
                    (nuint)i,
                    handle.AddrOfPinnedObject(),
                    bytesPerRow,
                    bytesPerImage);
            }
            finally
            {
                handle.Free();
            }
        }

        // Create buffer for frame index uniform
        _frameIndexBuffer?.Dispose();
        _frameIndexBuffer = _device.CreateBuffer((nuint)sizeof(int), MTLResourceOptions.StorageModeShared);

        // Display first frame
        DisplayArrayFrame(0);
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
        _animationFrameCount = frameCount;
        _currentArrayFrameIndex = 0;

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

        _currentArrayFrameIndex = index;

        // Update frame index in buffer
        Marshal.WriteInt32(_frameIndexBuffer.Contents, index);

        Render();
    }

    private void CreateTextureFromBgra8(byte[] pixels, int width, int height)
    {
        if (_device == null) return;

        // Convert BGRA8 to RGBA16Float for the EDR pipeline
        var floatPixels = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int srcIdx = i * 4;
            int dstIdx = i * 4;
            // BGRA -> RGBA, convert to linear float (approximate sRGB decode)
            floatPixels[dstIdx + 0] = SrgbToLinear(pixels[srcIdx + 2] / 255.0f); // R
            floatPixels[dstIdx + 1] = SrgbToLinear(pixels[srcIdx + 1] / 255.0f); // G
            floatPixels[dstIdx + 2] = SrgbToLinear(pixels[srcIdx + 0] / 255.0f); // B
            floatPixels[dstIdx + 3] = pixels[srcIdx + 3] / 255.0f; // A (linear)
        }

        CreateTextureFromFloat32(floatPixels, width, height);
    }

    private void CreateTextureFromFloat32(float[] pixels, int width, int height)
    {
        if (_device == null) return;

        using var textureDescriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(
            MTLPixelFormat.RGBA32Float,
            (nuint)width,
            (nuint)height,
            false);
        textureDescriptor.Usage = MTLTextureUsage.ShaderRead;
        textureDescriptor.StorageMode = MTLStorageMode.Shared;

        _imageTexture?.Dispose();
        _imageTexture = _device.CreateTexture(textureDescriptor);

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            _imageTexture?.ReplaceRegion(
                new MTLRegion(new MTLOrigin(0, 0, 0), new MTLSize((nint)width, (nint)height, 1)),
                0,
                handle.AddrOfPinnedObject(),
                (nuint)(width * 4 * sizeof(float)));
        }
        finally
        {
            handle.Free();
        }
    }

    private static float SrgbToLinear(float srgb)
    {
        if (srgb <= 0.04045f)
            return srgb / 12.92f;
        return MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
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
        passDescriptor.ColorAttachments[0].ClearColor = new MTLClearColor(0.1, 0.1, 0.1, 1.0);

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

        // Calculate scale to maintain aspect ratio and apply zoom
        var viewAspect = Bounds.Width / Bounds.Height;
        var imageAspect = (nfloat)_imageWidth / _imageHeight;

        float scaleX, scaleY;
        if (imageAspect > viewAspect)
        {
            scaleX = (float)_zoom;
            scaleY = (float)(_zoom * viewAspect / imageAspect);
        }
        else
        {
            scaleX = (float)(_zoom * imageAspect / viewAspect);
            scaleY = (float)_zoom;
        }

        // Uniforms: scale and offset
        float[] uniforms = [scaleX, scaleY, 0.0f, 0.0f];
        var uniformHandle = GCHandle.Alloc(uniforms, GCHandleType.Pinned);
        try
        {
            encoder.SetVertexBytes(uniformHandle.AddrOfPinnedObject(), (nuint)(uniforms.Length * sizeof(float)), 1);
        }
        finally
        {
            uniformHandle.Free();
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

    public void ClearImage()
    {
        _imageTexture?.Dispose();
        _imageTexture = null;

        // Dispose animation resources
        _animationTextureArray?.Dispose();
        _animationTextureArray = null;
        _frameIndexBuffer?.Dispose();
        _frameIndexBuffer = null;
        _decodingBuffer?.Dispose();
        _decodingBuffer = null;
        _decodingBufferPtr = nint.Zero;
        _decodingPixelCount = 0;
        _animationFrameCount = 0;

        _imageWidth = 0;
        _imageHeight = 0;
        _isHdr = false;
        SetNeedsDisplayInRect(Bounds);
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
