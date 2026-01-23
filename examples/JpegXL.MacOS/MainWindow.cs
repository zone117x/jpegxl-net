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
    private List<AnimationFrame>? _frames;
    private int _currentFrameIndex;
    private NSTimer? _animationTimer;
    private bool _isPlaying;
    private JxlBasicInfo? _currentInfo;

    private record AnimationFrame(float[] Pixels, float DurationMs);

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
        if (_frames == null || _frames.Count <= 1) return;

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

            var bytes = File.ReadAllBytes(path);

            // Get basic info first
            using var infoDecoder = new JxlDecoder();
            infoDecoder.SetInput(bytes);
            var info = infoDecoder.ReadInfo();
            _currentInfo = info;

            var isHdr = info.IsHdr;

            _frames = null;

            if (info.IsAnimated)
            {
                // Decode all frames as HDR (Float32 RGBA)
                _frames = DecodeAnimatedImage(bytes, info, isHdr);

                if (_frames.Count > 0)
                {
                    _currentFrameIndex = 0;
                    DisplayFrame(0);

                    if (_frames.Count > 1)
                    {
                        StartAnimation();
                    }

                    var formatStr = isHdr ? "HDR" : "Animated";
                    _infoLabel!.StringValue = $"{info.Width}×{info.Height} | {info.BitsPerSample}bpp | {_frames.Count} frames | {formatStr}";
                }
            }
            else
            {
                // Static image - decode as Float32 for HDR pipeline
                var pixels = DecodeStaticImage(bytes, info, isHdr);
                _metalView!.SetImageHdr(pixels, (int)info.Width, (int)info.Height);

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

    private float[] DecodeStaticImage(byte[] data, JxlBasicInfo info, bool isHdr)
    {
        var options = new JxlDecodeOptions
        {
            // Premultiply alpha so transparent regions blend to black
            PremultiplyAlpha = true
        };

        // For HDR images, we DON'T tone map - we pass the HDR values directly to the EDR display
        // For SDR images, we just decode normally
        // The Metal view handles the linear color space conversion

        var image = JxlImage.Decode(data, JxlPixelFormat.Rgba32F, options);
        return ConvertToFloatArray(image.GetPixelArray(), (int)info.Width, (int)info.Height);
    }

    private List<AnimationFrame> DecodeAnimatedImage(byte[] data, JxlBasicInfo info, bool isHdr)
    {
        var frames = new List<AnimationFrame>();
        var width = (int)info.Width;
        var height = (int)info.Height;
        var bufferSize = width * height * 4 * sizeof(float);
        var pixels = new byte[bufferSize];

        var options = new JxlDecodeOptions
        {
            // Premultiply alpha so transparent regions blend to black
            PremultiplyAlpha = true
        };

        using var decoder = new JxlDecoder(options);
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

            float duration = 0;
            if (evt == JxlDecoderEvent.HaveFrameHeader)
            {
                var header = decoder.GetFrameHeader();
                duration = header.DurationMs;
                evt = decoder.Process();
            }

            if (evt == JxlDecoderEvent.NeedOutputBuffer)
            {
                evt = decoder.ReadPixels(pixels);

                if (evt == JxlDecoderEvent.FrameComplete)
                {
                    var floatPixels = ConvertToFloatArray(pixels, width, height);
                    frames.Add(new AnimationFrame(floatPixels, duration > 0 ? duration : 100f));
                }
            }

            if (frames.Count > 1000) break; // Safety limit
        }

        return frames;
    }

    private static float[] ConvertToFloatArray(byte[] bytes, int width, int height)
    {
        var floats = new float[width * height * 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private void DisplayFrame(int index)
    {
        if (_frames == null || index >= _frames.Count || _currentInfo == null) return;

        var frame = _frames[index];
        _metalView!.SetImageHdr(frame.Pixels, (int)_currentInfo.Value.Width, (int)_currentInfo.Value.Height);
        _frameLabel!.StringValue = $"Frame {index + 1}/{_frames.Count}";
    }

    private void StartAnimation()
    {
        if (_frames == null || _frames.Count <= 1) return;

        StopAnimation();

        var interval = Math.Max(0.016, _frames[_currentFrameIndex].DurationMs / 1000.0);
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
        if (_frames == null || _frames.Count == 0) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
        DisplayFrame(_currentFrameIndex);

        // Update timer interval for next frame
        if (_animationTimer != null)
        {
            _animationTimer.Invalidate();
            var nextDuration = _frames[_currentFrameIndex].DurationMs / 1000.0;
            _animationTimer = NSTimer.CreateScheduledTimer(Math.Max(0.016, nextDuration), true, timer => OnAnimationTick());
        }
    }

    private void UpdatePlayPauseButton()
    {
        var hasAnimation = _frames != null && _frames.Count > 1;
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
