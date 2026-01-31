using System.Diagnostics;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using JpegXL.Net;

namespace JpegXL.MacOS;

public class MainWindow : NSWindow
{
    private const string DefaultTitle = "JPEG XL Viewer ";

    private HdrMetalView? _metalView;
    private NSTextField? _statusLabel;
    private NSTextField? _dimensionsLabel;
    private NSTextField? _profileLabel;
    private NSTextField? _hdrLabel;
    private NSTextField? _frameLabel;
    private NSButton? _playPauseButton;

    // Animation support
    private float[]? _frameDurations;  // Only store durations, pixels are on GPU
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
        Title = DefaultTitle;
        MinSize = new CGSize(400, 300);

        // Set up toolbar in title bar
        var toolbar = new NSToolbar("MainToolbar")
        {
            DisplayMode = NSToolbarDisplayMode.Icon,
            AllowsUserCustomization = false
        };
        toolbar.Delegate = new ToolbarDelegate(this);
        Toolbar = toolbar;
        TitleVisibility = NSWindowTitleVisibility.Visible;

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

        // Metal view for image display (now extends to top since toolbar is in title bar)
        _metalView = new HdrMetalView(new CGRect(0, 40, 900, 660))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            OnZoomChanged = _ => UpdateStatus()
        };
        contentView.AddSubview(_metalView);

        // Status bar - horizontal stack view for responsive layout
        var statusBar = new NSStackView(new CGRect(0, 0, 900, 40))
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Distribution = NSStackViewDistribution.Fill,
            Spacing = 12,
            EdgeInsets = new NSEdgeInsets(10, 12, 10, 12),
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin
        };
        statusBar.WantsLayer = true;
        statusBar.Layer!.BackgroundColor = new CGColor(0.15f, 0.15f, 0.15f, 1.0f);

        // Status bar labels ordered: Zoom | Dimensions | Profile | HDR | Frame

        // Zoom label with fixed minimum width (measured from font metrics)
        _statusLabel = CreateStackLabel("Zoom: 100%", NSColor.White, hugging: true);
        var font = NSFont.SystemFontOfSize(12)!;
        var maxZoomText = new NSAttributedString("Zoom: 10000%",
            new NSDictionary(NSStringAttributeKey.Font, font));
        _statusLabel.WidthAnchor.ConstraintGreaterThanOrEqualTo(maxZoomText.Size.Width).Active = true;
        statusBar.AddArrangedSubview(_statusLabel);

        // Dimensions label (content-hugging)
        _dimensionsLabel = CreateStackLabel("", NSColor.Gray, hugging: true);
        statusBar.AddArrangedSubview(_dimensionsLabel);

        // Color profile label (expands to fill, truncates with ellipsis)
        _profileLabel = CreateStackLabel("", NSColor.Gray, hugging: false);
        statusBar.AddArrangedSubview(_profileLabel);

        // HDR label (content-hugging, initially hidden)
        _hdrLabel = CreateStackLabel("", NSColor.Orange, hugging: true);
        _hdrLabel.Hidden = true;
        statusBar.AddArrangedSubview(_hdrLabel);

        // Frame label (content-hugging, initially hidden)
        _frameLabel = CreateStackLabel("", NSColor.SystemGreen, hugging: true);
        _frameLabel.Hidden = true;
        statusBar.AddArrangedSubview(_frameLabel);

        // Play/Pause button (initially hidden, shown for animations)
        _playPauseButton = NSButton.CreateButton("Pause", PlayPause);
        _playPauseButton.Hidden = true;
        _playPauseButton.SetContentHuggingPriorityForOrientation(750f, NSLayoutConstraintOrientation.Horizontal);
        statusBar.AddArrangedSubview(_playPauseButton);

        contentView.AddSubview(statusBar);

        ContentView = contentView;
    }

    private static NSTextField CreateStackLabel(string text, NSColor color, bool hugging)
    {
        var label = new NSTextField
        {
            StringValue = text,
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            TextColor = color,
            Font = NSFont.SystemFontOfSize(12)!,
            LineBreakMode = NSLineBreakMode.TruncatingTail,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        // Content hugging: high = stay small, low = expand to fill space
        var priority = hugging ? 750f : 250f;
        label.SetContentHuggingPriorityForOrientation(priority, NSLayoutConstraintOrientation.Horizontal);

        // Compression resistance: hugging labels resist shrinking, expanding labels allow truncation
        label.SetContentCompressionResistancePriority(hugging ? 750f : 250f,
            NSLayoutConstraintOrientation.Horizontal);

        return label;
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

    private void ActualSize()
    {
        if (_metalView != null)
        {
            _metalView.ResetView();
            UpdateStatus();
        }
    }

    private void ZoomToFit()
    {
        if (_metalView != null)
        {
            _metalView.ZoomToFit();
            UpdateStatus();
        }
    }

    private void PlayPause()
    {
        if (_frameDurations == null || _frameDurations.Length <= 1) return;

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

    public async void LoadImage(string path)
    {
        try
        {
            StopAnimation();
            var filename = Path.GetFileName(path);
            Subtitle = $"Loading {filename}";
            _hdrLabel!.Hidden = true;
            _frameLabel!.Hidden = true;

            Console.WriteLine($"Loading image: {path}");

            var options = new JxlDecodeOptions {
                PremultiplyAlpha = true,
                PixelFormat = JxlPixelFormat.Rgba32F
            };

            // Single decoder for entire load operation
            // Using SetInputFileAsync reads directly into native memory on a background thread,
            // keeping the UI responsive during file I/O
            using var decoder = new JxlDecoder(options);
            await decoder.SetInputFileAsync(path);

            var info = decoder.ReadInfo();
            _currentInfo = info;

            // Get color profile description for status bar
            string colorProfileDesc;
            using (var profile = decoder.GetEmbeddedColorProfile())
            {
                Console.WriteLine($"[ColorProfile] IsIcc={profile.IsIcc}");
                if (profile.IsIcc)
                {
                    var iccName = IccProfileParser.TryGetDescription(profile.IccData);
                    Console.WriteLine($"[ColorProfile] ICC name from parser: '{iccName}'");
                    colorProfileDesc = iccName != null ? $"ICC: {iccName}" : "ICC";
                }
                else
                {
                    colorProfileDesc = profile.GetDescription();
                    Console.WriteLine($"[ColorProfile] GetDescription returned: '{colorProfileDesc}'");
                }
            }

            Console.WriteLine($"[ColorProfile] Final desc before check: '{colorProfileDesc}'");
            if (string.IsNullOrWhiteSpace(colorProfileDesc))
            {
                colorProfileDesc = "(no color profile)";
            }
            Console.WriteLine($"[ColorProfile] Final desc after check: '{colorProfileDesc}'");

            var isHdr = info.IsHdr;

            _frameDurations = null;

            if (info.IsAnimated)
            {
                // First pass: get metadata (uses SkipFrame internally, no pixel buffers)
                var animationMetadata = decoder.ParseFrameMetadata();

                // Rewind for pixel decoding (pixel_format is preserved)
                decoder.Rewind();

                // Decode all frames directly to GPU-shared memory
                _frameDurations = DecodeAnimatedImageToGpu(decoder, info, animationMetadata);

                if (_frameDurations.Length > 0)
                {
                    _currentFrameIndex = 0;

                    if (_frameDurations.Length > 1)
                    {
                        StartAnimation();
                    }
                }
            }
            else
            {
                // Static image - decode directly to GPU-shared memory (zero-copy on Apple Silicon)
                DecodeStaticImageToGpu(decoder, info);
            }

            // Set status bar labels
            _dimensionsLabel!.StringValue = $"{info.Size.Width}×{info.Size.Height}";
            _profileLabel!.StringValue = colorProfileDesc;

            // Show HDR info
            _hdrLabel!.Hidden = !isHdr;
            if (isHdr)
            {
                _hdrLabel.StringValue = $"HDR: {info.ToneMapping.IntensityTarget:F0} nits";

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

            Subtitle = filename;
            _metalView!.ResetView();  // Display at 1:1 pixel ratio
            UpdateStatus();  // Show zoom level
            UpdatePlayPauseButton();
        }
        catch (Exception ex)
        {
            Subtitle = "";
            _statusLabel!.StringValue = $"Error: {ex.Message}";
            _dimensionsLabel!.StringValue = "";
            _profileLabel!.StringValue = "";
            _hdrLabel!.Hidden = true;
        }
    }

    /// <summary>
    /// Decodes a static image directly into GPU-shared memory.
    /// On Apple Silicon, this eliminates the managed array allocation and uses unified memory.
    /// </summary>
    private void DecodeStaticImageToGpu(JxlDecoder decoder, JxlBasicInfo info)
    {
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;

        // Decode directly into GPU-shared memory
        _metalView!.DecodeDirectToGpu(width, height, pixelSpan =>
        {
            decoder.GetPixels(MemoryMarshal.AsBytes(pixelSpan));
        });
    }

    /// <summary>
    /// Decodes an animated image directly into GPU-shared memory.
    /// Returns only the frame durations - pixel data goes directly to GPU texture array.
    /// Expects decoder to be already rewound with pixel format set.
    /// </summary>
    private float[] DecodeAnimatedImageToGpu(JxlDecoder decoder, JxlBasicInfo info, JxlAnimationMetadata metadata)
    {
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;
        var frameCount = metadata.Frames.Count;
        var durations = metadata.GetFrameDurationsMs();
        
        if (durations.Length == 0) return durations;

        // Prepare GPU texture array
        _metalView!.PrepareAnimationTextures(frameCount, width, height);

        // Decode all frames sequentially
        // Use frameCount from metadata instead of HasMoreFrames() which requires WithImageInfo state
        int frameIndex = 0;
        while (frameIndex < frameCount)
        {
            var evt = decoder.Process();

            // Handle Initialized→WithImageInfo transition and incomplete input
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
        if (_frameDurations == null || index >= _frameDurations.Length || _currentInfo == null) return;

        // All frames are in GPU texture array - just switch the index
        _metalView!.DisplayArrayFrame(index);
        _frameLabel!.StringValue = $"Frame {index + 1}/{_frameDurations.Length}";
    }

    private void StartAnimation()
    {
        if (_frameDurations == null || _frameDurations.Length <= 1) return;

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
        if (_frameDurations == null || _frameDurations.Length == 0) return;

        var elapsed = (DateTime.UtcNow - _frameStartTime).TotalMilliseconds;
        var frameDuration = _frameDurations[_currentFrameIndex];

        if (elapsed >= frameDuration)
        {
            _currentFrameIndex = (_currentFrameIndex + 1) % _frameDurations.Length;
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
        var hasAnimation = _frameDurations != null && _frameDurations.Length > 1;
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

    private class ToolbarDelegate : NSToolbarDelegate
    {
        private readonly MainWindow _window;

        private static readonly string[] ItemIdentifiers =
        [
            "OpenFile",
            NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
            "ZoomIn", "ZoomOut", "ActualSize", "Fit"
        ];

        public ToolbarDelegate(MainWindow window) => _window = window;

        public override string[] AllowedItemIdentifiers(NSToolbar toolbar) => ItemIdentifiers;
        public override string[] DefaultItemIdentifiers(NSToolbar toolbar) => ItemIdentifiers;

        public override NSToolbarItem WillInsertItem(NSToolbar toolbar, string itemIdentifier, bool willBeInserted)
        {
            var item = new NSToolbarItem(itemIdentifier);

            switch (itemIdentifier)
            {
                case "OpenFile":
                    item.Label = "Open";
                    item.ToolTip = "Open a JPEG XL file";
                    item.View = NSButton.CreateButton("Open File...", _window.OpenFile);
                    item.Navigational = true;  // Positions left of title
                    break;
                case "ZoomIn":
                    item.Label = "Zoom In";
                    item.ToolTip = "Zoom in";
                    item.View = NSButton.CreateButton("Zoom In", _window.ZoomIn);
                    break;
                case "ZoomOut":
                    item.Label = "Zoom Out";
                    item.ToolTip = "Zoom out";
                    item.View = NSButton.CreateButton("Zoom Out", _window.ZoomOut);
                    break;
                case "ActualSize":
                    item.Label = "1:1";
                    item.ToolTip = "Actual size (1:1 pixels)";
                    item.View = NSButton.CreateButton("1:1", _window.ActualSize);
                    break;
                case "Fit":
                    item.Label = "Fit";
                    item.ToolTip = "Fit image to window";
                    item.View = NSButton.CreateButton("Fit", _window.ZoomToFit);
                    break;
            }

            return item;
        }
    }
}
