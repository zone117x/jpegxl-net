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
    private bool _isPlaying;
    private JxlBasicInfo? _currentInfo;

    public MainWindow() : base(
        new CGRect(100, 100, 900, 700),
        NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
        NSBackingStore.Buffered,
        false)
    {
        Title = "JPEG XL Viewer (HDR)";
        MinSize = new CGSize(400, 300);

        CreateUI();
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
    /// Decodes an animated image directly into GPU-shared memory.
    /// Returns only the frame durations - pixel data goes directly to GPU texture array.
    /// </summary>
    private List<float> DecodeAnimatedImageToGpu(byte[] data, JxlBasicInfo info)
    {

        var durations = new List<float>();
        var width = (int)info.Width;
        var height = (int)info.Height;
        var pixelCount = width * height * 4;

        var options = new JxlDecodeOptions
        {
            PremultiplyAlpha = true
        };

        // We need a scratch buffer to skip frames (decoder requires output buffer)
        var scratchBuffer = new float[pixelCount];

        // First pass: count frames and collect durations
        using (var decoder = new JxlDecoder(options))
        {
            decoder.SetInput(data);
            decoder.SetPixelFormat(JxlPixelFormat.Rgba32F);
            decoder.ReadInfo();

            while (decoder.HasMoreFrames())
            {
                var evt = decoder.Process();

                while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                {
                    evt = decoder.Process();
                }

                if (evt == JxlDecoderEvent.Complete)
                    break;

                float duration = 100f;
                if (evt == JxlDecoderEvent.HaveFrameHeader)
                {
                    var header = decoder.GetFrameHeader();
                    duration = header.DurationMs > 0 ? header.DurationMs : 100f;
                    durations.Add(duration);
                    evt = decoder.Process();
                }

                // Must provide buffer to skip frame - decoder needs somewhere to write
                if (evt == JxlDecoderEvent.NeedOutputBuffer)
                {
                    decoder.ReadPixels(scratchBuffer.AsSpan());
                }

                if (durations.Count > 1000) break; // Safety limit
            }
        }

        if (durations.Count == 0) return durations;

        // Second pass: decode frames directly to GPU
        _metalView!.DecodeAnimationDirectToGpu(durations.Count, width, height, (index, pixelSpan) =>
        {

            // Create a new decoder for this frame
            using var decoder = new JxlDecoder(options);
            decoder.SetInput(data);
            decoder.SetPixelFormat(JxlPixelFormat.Rgba32F);
            decoder.ReadInfo();

            // Skip to the target frame
            var currentFrame = 0;
            while (decoder.HasMoreFrames())
            {
                var evt = decoder.Process();

                while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                {
                    evt = decoder.Process();
                }

                if (evt == JxlDecoderEvent.Complete)
                    break;

                if (evt == JxlDecoderEvent.HaveFrameHeader)
                {
                    evt = decoder.Process();
                }

                if (evt == JxlDecoderEvent.NeedOutputBuffer)
                {
                    if (currentFrame == index)
                    {
                        // Decode this frame directly to GPU-shared memory
                        decoder.ReadPixels(pixelSpan);
                        return;
                    }
                    else
                    {
                        // Skip this frame by decoding to scratch buffer
                        decoder.ReadPixels(scratchBuffer.AsSpan());
                    }
                    currentFrame++;
                }
            }
        });

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

        var interval = Math.Max(0.016, _frameDurations[_currentFrameIndex] / 1000.0);
        _animationTimer = NSTimer.CreateScheduledTimer(interval, true, timer => OnAnimationTick());
        _isPlaying = true;
    }

    private void StopAnimation()
    {
        _animationTimer?.Invalidate();
        _animationTimer = null;
        _isPlaying = false;
    }

    private void OnAnimationTick()
    {
        if (_frameDurations == null || _frameDurations.Count == 0) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frameDurations.Count;
        DisplayFrame(_currentFrameIndex);

        // Update timer interval for next frame
        if (_animationTimer != null)
        {
            _animationTimer.Invalidate();
            var nextDuration = _frameDurations[_currentFrameIndex] / 1000.0;
            _animationTimer = NSTimer.CreateScheduledTimer(Math.Max(0.016, nextDuration), true, timer => OnAnimationTick());
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
            _metalView?.Dispose();
        }
        base.Dispose(disposing);
    }
}
