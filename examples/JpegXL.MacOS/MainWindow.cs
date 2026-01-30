using System.Diagnostics;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using JpegXL.Net;

namespace JpegXL.MacOS;

public class MainWindow : NSWindow
{
    private HdrMetalView? _metalView;
    private NSTextField? _statusLabel;
    private NSTextField? _infoLabel;
    private NSTextField? _hdrLabel;
    private NSTextField? _frameLabel;
    private NSButton? _playPauseButton;

    // Animation support
    private List<float>? _frameDurations;  // Only store durations, pixels are on GPU
    private int _currentFrameIndex;
    private NSTimer? _animationTimer;
    private DateTime _frameStartTime;
    private bool _isPlaying;
    private JxlBasicInfo? _currentInfo;

    // Memory monitoring
    private NSTimer? _memoryTimer;

    public MainWindow() : base(
        new CGRect(100, 100, 900, 700),
        NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
        NSBackingStore.Buffered,
        false)
    {
        Title = "JPEG XL Viewer (HDR)";
        MinSize = new CGSize(400, 300);

        CreateUI();
        #if DEBUG
        StartMemoryMonitor();
        #endif
    }

    private void StartMemoryMonitor()
    {
        _memoryTimer = NSTimer.CreateScheduledTimer(2.0, true, OnMemoryTick);
    }

    private static void OnMemoryTick(NSTimer _)
    {
        using var process = Process.GetCurrentProcess();
        var totalMB = process.WorkingSet64 / 1024.0 / 1024.0;
        var managedMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
        var unmanagedMB = totalMB - managedMB;

        Console.WriteLine($"[Memory] Total: {totalMB:F1} MB | Managed: {managedMB:F1} MB | Unmanaged: {unmanagedMB:F1} MB");
    }

    private void CreateUI()
    {
        var contentView = new NSView(new CGRect(0, 0, 900, 700))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };

        // Toolbar area
        var toolbar = new NSView(new CGRect(0, 660, 900, 40))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
        };
        toolbar.WantsLayer = true;
        toolbar.Layer!.BackgroundColor = NSColor.WindowBackground.CGColor;

        nfloat buttonX = 10;

        var openButton = NSButton.CreateButton("Open File...", () => OpenFile());
        openButton.Frame = new CGRect(buttonX, 5, 100, 30);
        toolbar.AddSubview(openButton);
        buttonX += 110;

        var zoomInButton = NSButton.CreateButton("Zoom In", () => ZoomIn());
        zoomInButton.Frame = new CGRect(buttonX, 5, 80, 30);
        toolbar.AddSubview(zoomInButton);
        buttonX += 90;

        var zoomOutButton = NSButton.CreateButton("Zoom Out", () => ZoomOut());
        zoomOutButton.Frame = new CGRect(buttonX, 5, 80, 30);
        toolbar.AddSubview(zoomOutButton);
        buttonX += 90;

        var resetZoomButton = NSButton.CreateButton("Reset", () => ResetZoom());
        resetZoomButton.Frame = new CGRect(buttonX, 5, 60, 30);
        toolbar.AddSubview(resetZoomButton);
        buttonX += 70;

        _playPauseButton = NSButton.CreateButton("Play", () => PlayPause());
        _playPauseButton.Frame = new CGRect(buttonX, 5, 70, 30);
        _playPauseButton.Hidden = true;
        toolbar.AddSubview(_playPauseButton);

        contentView.AddSubview(toolbar);

        // Metal view for image display
        _metalView = new HdrMetalView(new CGRect(0, 40, 900, 620))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        contentView.AddSubview(_metalView);

        // Status bar
        var statusBar = new NSView(new CGRect(0, 0, 900, 40))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin
        };
        statusBar.WantsLayer = true;
        statusBar.Layer!.BackgroundColor = new CGColor(0.15f, 0.15f, 0.15f, 1.0f);

        _statusLabel = CreateLabel("Ready", new CGRect(10, 10, 200, 20), NSColor.White);
        statusBar.AddSubview(_statusLabel);

        _infoLabel = CreateLabel("", new CGRect(220, 10, 300, 20), NSColor.Gray);
        statusBar.AddSubview(_infoLabel);

        _frameLabel = CreateLabel("", new CGRect(530, 10, 150, 20), NSColor.SystemGreen);
        _frameLabel.Hidden = true;
        statusBar.AddSubview(_frameLabel);

        _hdrLabel = CreateLabel("", new CGRect(690, 10, 200, 20), NSColor.Orange);
        _hdrLabel.Hidden = true;
        statusBar.AddSubview(_hdrLabel);

        contentView.AddSubview(statusBar);

        ContentView = contentView;
    }

    private static NSTextField CreateLabel(string text, CGRect frame, NSColor color)
    {
        return new NSTextField(frame)
        {
            StringValue = text,
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            TextColor = color,
            Font = NSFont.SystemFontOfSize(12)!
        };
    }

    private void OpenFile()
    {
        var panel = NSOpenPanel.OpenPanel;
        var jxlType = UniformTypeIdentifiers.UTType.CreateFromExtension("jxl");
        if (jxlType != null)
        {
            panel.AllowedContentTypes = [jxlType];
        }
        panel.AllowsMultipleSelection = false;
        panel.CanChooseDirectories = false;

        if (panel.RunModal() == 1 && panel.Url?.Path != null)
        {
            LoadImage(panel.Url.Path);
        }
    }

    private void ZoomIn()
    {
        if (_metalView != null)
        {
            _metalView.Zoom *= 1.25f;
            UpdateStatus();
        }
    }

    private void ZoomOut()
    {
        if (_metalView != null)
        {
            _metalView.Zoom /= 1.25f;
            UpdateStatus();
        }
    }

    private void ResetZoom()
    {
        if (_metalView != null)
        {
            _metalView.Zoom = 1.0f;
            UpdateStatus();
        }
    }

    private void PlayPause()
    {
        if (_frameDurations == null || _frameDurations.Count <= 1) return;

        if (_isPlaying)
        {
            StopAnimation();
        }
        else
        {
            StartAnimation();
        }
        UpdatePlayPauseButton();
    }

    public void LoadImage(string path)
    {
        try
        {
            StopAnimation();
            _statusLabel!.StringValue = "Loading...";
            _hdrLabel!.Hidden = true;
            _frameLabel!.Hidden = true;
        
            Console.WriteLine($"Loading image: {path}");
            var bytes = File.ReadAllBytes(path);

            // Get basic info first
            using var infoDecoder = new JxlDecoder();
            infoDecoder.SetInput(bytes);
            var info = infoDecoder.ReadInfo();
            _currentInfo = info;

            var isHdr = info.IsHdr;

            _frameDurations = null;

            if (info.IsAnimated)
            {
                // Decode all frames directly to GPU-shared memory
                _frameDurations = DecodeAnimatedImageToGpu(bytes, info);

                if (_frameDurations.Count > 0)
                {
                    _currentFrameIndex = 0;

                    if (_frameDurations.Count > 1)
                    {
                        StartAnimation();
                    }

                    var formatStr = isHdr ? "HDR" : "Animated";
                    _infoLabel!.StringValue = $"{info.Width}×{info.Height} | {info.BitsPerSample}bpp | {_frameDurations.Count} frames | {formatStr}";
                }
            }
            else
            {
                // Static image - decode directly to GPU-shared memory (zero-copy on Apple Silicon)
                DecodeStaticImageToGpu(bytes, info);

                var formatStr = isHdr ? "HDR" : (info.HasAlpha ? "RGBA" : "RGB");
                _infoLabel!.StringValue = $"{info.Width}×{info.Height} | {info.BitsPerSample}bpp | {formatStr}";
            }

            // Show HDR info
            _hdrLabel!.Hidden = !isHdr;
            if (isHdr)
            {
                _hdrLabel.StringValue = $"HDR: {info.IntensityTarget:F0} nits";

                // Query EDR headroom
                var screen = Screen ?? NSScreen.MainScreen;
                if (screen != null)
                {
                    var headroom = screen.MaximumExtendedDynamicRangeColorComponentValue;
                    if (headroom > 1.0)
                    {
                        _hdrLabel.StringValue += $" | EDR: {headroom:F1}x";
                    }
                }
            }

            _statusLabel!.StringValue = $"Loaded: {Path.GetFileName(path)}";
            _metalView!.Zoom = 1.0f;
            UpdatePlayPauseButton();
        }
        catch (Exception ex)
        {
            _statusLabel!.StringValue = $"Error: {ex.Message}";
            _infoLabel!.StringValue = "";
            _hdrLabel!.Hidden = true;
        }
    }

    /// <summary>
    /// Decodes a static image directly into GPU-shared memory.
    /// On Apple Silicon, this eliminates the managed array allocation and uses unified memory.
    /// </summary>
    private void DecodeStaticImageToGpu(byte[] data, JxlBasicInfo info)
    {
        var options = new JxlDecodeOptions
        {
            PremultiplyAlpha = true
        };

        var width = (int)info.Width;
        var height = (int)info.Height;

        // Use the low-level decoder to decode directly into GPU-shared memory
        _metalView!.DecodeDirectToGpu(width, height, pixelSpan =>
        {
            using var decoder = new JxlDecoder(options);
            decoder.SetInput(data);
            decoder.SetPixelFormat(JxlPixelFormat.Rgba32F);
            decoder.ReadInfo();
            decoder.GetPixels(MemoryMarshal.AsBytes(pixelSpan));
        });
    }

    /// <summary>
    /// Decodes an animated image directly into GPU-shared memory using a single decoder.
    /// Returns only the frame durations - pixel data goes directly to GPU texture array.
    /// </summary>
    private List<float> DecodeAnimatedImageToGpu(byte[] data, JxlBasicInfo info)
    {
        var durations = new List<float>();
        var width = (int)info.Width;
        var height = (int)info.Height;

        var options = new JxlDecodeOptions
        {
            PremultiplyAlpha = true
        };

        // First pass: collect frame metadata using SkipFrame (no pixel buffer needed!)
        using (var metaDecoder = new JxlDecoder(options))
        {
            metaDecoder.SetInput(data);
            metaDecoder.SetPixelFormat(JxlPixelFormat.Rgba32F);
            metaDecoder.ReadInfo();

            while (metaDecoder.HasMoreFrames())
            {
                var evt = metaDecoder.Process();

                while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                    evt = metaDecoder.Process();

                if (evt == JxlDecoderEvent.Complete)
                    break;

                if (evt == JxlDecoderEvent.HaveFrameHeader)
                {
                    var header = metaDecoder.GetFrameHeader();
                    float duration = header.DurationMs > 0 ? header.DurationMs : 100f;
                    durations.Add(duration);
                    evt = metaDecoder.Process();
                }

                // Skip frame without allocating pixel buffer
                if (evt == JxlDecoderEvent.NeedOutputBuffer)
                    metaDecoder.SkipFrame();

                if (durations.Count > 1000) break; // Safety limit
            }
        }

        if (durations.Count == 0) return durations;

        // Prepare GPU texture array
        _metalView!.PrepareAnimationTextures(durations.Count, width, height);

        // Second pass: decode all frames sequentially with a single decoder
        using var decoder = new JxlDecoder(options);
        decoder.SetInput(data);
        decoder.SetPixelFormat(JxlPixelFormat.Rgba32F);
        decoder.ReadInfo();

        int frameIndex = 0;
        while (decoder.HasMoreFrames() && frameIndex < durations.Count)
        {
            var evt = decoder.Process();

            while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                evt = decoder.Process();

            if (evt == JxlDecoderEvent.Complete)
                break;

            if (evt == JxlDecoderEvent.HaveFrameHeader)
                evt = decoder.Process();

            if (evt == JxlDecoderEvent.NeedOutputBuffer)
            {
                // Decode frame directly to GPU-shared memory
                _metalView.DecodeFrameToGpu(frameIndex, pixelSpan =>
                {
                    decoder.ReadPixels(pixelSpan);
                });
                frameIndex++;
            }
        }

        _metalView.FinishAnimationSetup();
        return durations;
    }

    private void DisplayFrame(int index)
    {
        if (_frameDurations == null || index >= _frameDurations.Count || _currentInfo == null) return;

        // All frames are in GPU texture array - just switch the index
        _metalView!.DisplayArrayFrame(index);
        _frameLabel!.StringValue = $"Frame {index + 1}/{_frameDurations.Count}";
    }

    private void StartAnimation()
    {
        if (_frameDurations == null || _frameDurations.Count <= 1) return;

        StopAnimation();

        _frameStartTime = DateTime.UtcNow;
        // Single timer at ~60fps, never recreated during playback
        _animationTimer = NSTimer.CreateScheduledTimer(0.016, true, OnAnimationTick);
        _isPlaying = true;
    }

    private void StopAnimation()
    {
        _animationTimer?.Invalidate();
        _animationTimer?.Dispose();
        _animationTimer = null;
        _isPlaying = false;
    }

    private void OnAnimationTick(NSTimer _)
    {
        if (_frameDurations == null || _frameDurations.Count == 0) return;

        var elapsed = (DateTime.UtcNow - _frameStartTime).TotalMilliseconds;
        var frameDuration = _frameDurations[_currentFrameIndex];

        if (elapsed >= frameDuration)
        {
            _currentFrameIndex = (_currentFrameIndex + 1) % _frameDurations.Count;
            // Use autorelease pool to ensure Metal/Cocoa objects are cleaned up promptly
            using (new NSAutoreleasePool())
            {
                DisplayFrame(_currentFrameIndex);
            }
            _frameStartTime = DateTime.UtcNow;
        }
    }

    private void UpdatePlayPauseButton()
    {
        var hasAnimation = _frameDurations != null && _frameDurations.Count > 1;
        _playPauseButton!.Hidden = !hasAnimation;
        _frameLabel!.Hidden = !hasAnimation;

        if (hasAnimation)
        {
            _playPauseButton!.Title = _isPlaying ? "Pause" : "Play";
        }
    }

    private void UpdateStatus()
    {
        if (_metalView != null)
        {
            _statusLabel!.StringValue = $"Zoom: {_metalView.Zoom:P0}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            _memoryTimer?.Invalidate();
            _memoryTimer?.Dispose();
            _memoryTimer = null;
            _metalView?.Dispose();
        }
        base.Dispose(disposing);
    }
}
