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
    private IMTLTexture? _imageTexture;
    private IMTLBuffer? _vertexBuffer;

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
";

        NSError? error;
        var library = _device.CreateLibrary(shaderSource, new MTLCompileOptions(), out error);
        if (library == null || error != null)
        {
            Console.WriteLine($"Failed to create shader library: {error?.LocalizedDescription}");
            return;
        }

        var vertexFunction = library.CreateFunction("vertexShader");
        var fragmentFunction = library.CreateFunction("fragmentShader");

        var vertexDescriptor = new MTLVertexDescriptor();
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

        var pipelineDescriptor = new MTLRenderPipelineDescriptor
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

        CreateTextureFromFloat32(pixels, width, height);
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

        var textureDescriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(
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
        if (_metalLayer == null || _device == null || _commandQueue == null ||
            _pipelineState == null || _vertexBuffer == null || _imageTexture == null)
        {
            return;
        }

        // Update drawable size
        _metalLayer.DrawableSize = new CGSize(
            Bounds.Width * (_metalLayer.ContentsScale),
            Bounds.Height * (_metalLayer.ContentsScale));

        var drawable = _metalLayer.NextDrawable();
        if (drawable == null) return;

        var commandBuffer = _commandQueue.CommandBuffer();
        if (commandBuffer == null) return;

        var passDescriptor = new MTLRenderPassDescriptor();
        passDescriptor.ColorAttachments[0].Texture = drawable.Texture;
        passDescriptor.ColorAttachments[0].LoadAction = MTLLoadAction.Clear;
        passDescriptor.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        passDescriptor.ColorAttachments[0].ClearColor = new MTLClearColor(0.1, 0.1, 0.1, 1.0);

        var encoder = commandBuffer.CreateRenderCommandEncoder(passDescriptor);
        if (encoder == null) return;

        encoder.SetRenderPipelineState(_pipelineState);
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

        encoder.SetFragmentTexture(_imageTexture, 0);
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
        }
        base.Dispose(disposing);
    }
}
