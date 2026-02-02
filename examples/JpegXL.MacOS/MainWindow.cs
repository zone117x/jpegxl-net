using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppKit;
using CoreGraphics;
using Foundation;
using ImageIO;
using JpegXL.Net;
using UniformTypeIdentifiers;

namespace JpegXL.MacOS;

/// <summary>
/// Cached metadata from the loaded JXL image for display and clipboard copy.
/// </summary>
internal sealed class JxlImageMetadata
{
    public required string FilePath { get; init; }
    public required JxlBasicInfo BasicInfo { get; init; }
    public required string ColorProfileDescription { get; init; }
    public required bool IsHlg { get; init; }
    public required bool IsPq { get; init; }
    public int FrameCount { get; init; }
    public IccHeaderInfo? IccHeader { get; init; }
    public IccColorSpaceInfo? IccColorSpace { get; init; }
}

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
    private string? _currentFilePath;
    private DateTime _currentFileModified;

    // Memory monitoring
    private NSTimer? _memoryTimer;

    // Cached metadata for clipboard copy
    private JxlImageMetadata? _metadata;

    // Screen change notification observer
    private NSObject? _screenChangeObserver;

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

        // Subscribe to screen change notifications to update HDR/EDR when moving between monitors
        _screenChangeObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            NSWindow.DidChangeScreenNotification,
            OnScreenChanged,
            this);

        #if DEBUG
        StartMemoryMonitor();
        #endif
    }

    /// <summary>
    /// Called when the window moves to a different screen.
    /// Updates the Metal view's backing scale and reconfigures HDR settings for the new screen.
    /// </summary>
    private void OnScreenChanged(NSNotification notification)
    {
        _metalView?.UpdateContentsScale();
        UpdateHdrLabel();
    }

    /// <summary>
    /// Updates the HDR label and brightness scale based on current screen's EDR headroom.
    /// </summary>
    private void UpdateHdrLabel()
    {
        if (_metadata == null) return;

        var screen = Screen ?? NSScreen.MainScreen;
        var edrHeadroom = (float)(screen?.MaximumExtendedDynamicRangeColorComponentValue ?? 1.0);
        var intensityTarget = _metadata.BasicInfo.ToneMapping.IntensityTarget;

        if (_metadata.IsHlg)
        {
            _hdrLabel!.StringValue = $"HDR HLG: {intensityTarget:F0} nits";
            if (edrHeadroom > 1.0)
            {
                _hdrLabel.StringValue += $" | EDR: {edrHeadroom:F1}x";
            }
            Console.WriteLine($"[HDR] HLG system tone mapping (EDR headroom: {edrHeadroom:F1}x)");
        }
        else if (_metadata.IsPq)
        {
            _hdrLabel!.StringValue = $"HDR PQ: {intensityTarget:F0} nits";
            if (edrHeadroom > 1.0)
            {
                _hdrLabel.StringValue += $" | EDR: {edrHeadroom:F1}x";
            }
            Console.WriteLine($"[HDR] PQ system tone mapping (max: {intensityTarget} nits, EDR headroom: {edrHeadroom:F1}x)");
        }
        else if (_metadata.BasicInfo.IsHdr)
        {
            _hdrLabel!.StringValue = $"HDR: {intensityTarget:F0} nits";
            if (edrHeadroom > 1.0)
            {
                _hdrLabel.StringValue += $" | EDR: {edrHeadroom:F1}x";
            }

            // Manual HDR mode needs brightness scale recalculation
            // 203 nits is the SDR reference white level defined in ITU-R BT.2408
            const float SdrReferenceWhiteNits = 203f;
            var idealScale = intensityTarget / SdrReferenceWhiteNits;
            var brightnessScale = Math.Min(idealScale, edrHeadroom);
            _metalView!.HdrBrightnessScale = brightnessScale;
            Console.WriteLine($"[HDR] Linear mode with brightness scale: {brightnessScale:F2}x (EDR headroom: {edrHeadroom:F1}x)");
        }
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

        // Context menu for right-click
        var contextMenu = new NSMenu();
        contextMenu.AddItem(new NSMenuItem("Copy Metadata", (s, e) => CopyMetadata()));
        _metalView.Menu = contextMenu;

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

    private NSTextField CreateStackLabel(string text, NSColor color, bool hugging)
    {
        var label = new SelectableLabel(FormatMetadata)
        {
            StringValue = text,
            Editable = false,
            Selectable = true,
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

    private void ExportImage()
    {
        if (_metalView == null || _currentInfo == null || _currentFilePath == null)
        {
            return;
        }

        // If animated and playing, pause the animation
        var isAnimated = _frameDurations != null && _frameDurations.Length > 1;
        if (isAnimated && _isPlaying)
        {
            StopAnimation();
            UpdatePlayPauseButton();
        }

        // For animated images, inform the user about export behavior
        if (isAnimated)
        {
            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Export Animated Image",
                InformativeText = $"This file has {_frameDurations!.Length} frames. If exporting as GIF, all frames will be included. For other formats, only the current frame ({_currentFrameIndex + 1}) will be exported."
            };
            alert.AddButton("OK");
            alert.AddButton("Cancel");

            if (alert.RunModal() != (long)NSAlertButtonReturn.First)
            {
                return;
            }
        }

        // Validate source file still exists and hasn't changed
        if (!File.Exists(_currentFilePath))
        {
            ShowAlert("Source File Missing",
                "The original file no longer exists. Please reload the image.");
            return;
        }

        var currentModified = File.GetLastWriteTimeUtc(_currentFilePath);
        if (currentModified != _currentFileModified)
        {
            ShowAlert("Source File Changed",
                "The original file has been modified. Please reload to export the updated version.");
            return;
        }

        using var panel = NSSavePanel.SavePanel;
        panel.Title = "Export Image";
        panel.NameFieldStringValue = GetExportFilename();

        // Create accessory view with format selector and quality slider (like Preview.app)
        var accessoryView = new NSView(new CGRect(0, 0, 280, 80));

        // Format row
        var formatLabel = new NSTextField(new CGRect(0, 52, 60, 20))
        {
            StringValue = "Format:",
            Editable = false,
            Bordered = false,
            DrawsBackground = false
        };
        accessoryView.AddSubview(formatLabel);

        var formatPopup = new NSPopUpButton(new CGRect(65, 48, 120, 26), pullsDown: false);
        var exportFormats = new List<string> { "PNG", "JPEG", "TIFF" };
        // Only offer GIF export for animated images
        if (isAnimated) {
            exportFormats.Add("GIF");
        }
        formatPopup.AddItems(exportFormats.ToArray());   
        formatPopup.SelectItem(0);  // Default to PNG
        accessoryView.AddSubview(formatPopup);

        // Quality row (for JPEG only)
        var qualityLabel = new NSTextField(new CGRect(0, 22, 60, 20))
        {
            StringValue = "Quality:",
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            Hidden = true
        };
        accessoryView.AddSubview(qualityLabel);

        var qualitySlider = new NSSlider(new CGRect(65, 22, 140, 20))
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            DoubleValue = 0.85,
            Hidden = true
        };
        accessoryView.AddSubview(qualitySlider);

        var leastLabel = new NSTextField(new CGRect(65, 6, 40, 14))
        {
            StringValue = "Least",
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            Font = NSFont.SystemFontOfSize(10)!,
            TextColor = NSColor.SecondaryLabel,
            Hidden = true
        };
        accessoryView.AddSubview(leastLabel);

        var bestLabel = new NSTextField(new CGRect(175, 6, 30, 14))
        {
            StringValue = "Best",
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            Font = NSFont.SystemFontOfSize(10)!,
            TextColor = NSColor.SecondaryLabel,
            Hidden = true
        };
        accessoryView.AddSubview(bestLabel);

        // Show/hide quality controls based on format
        void UpdateQualityVisibility()
        {
            var isJpeg = formatPopup.TitleOfSelectedItem == "JPEG";
            qualityLabel.Hidden = !isJpeg;
            qualitySlider.Hidden = !isJpeg;
            leastLabel.Hidden = !isJpeg;
            bestLabel.Hidden = !isJpeg;
        }

        formatPopup.Activated += (s, e) =>
        {
            UpdateSaveExtension(panel, formatPopup);
            UpdateQualityVisibility();
        };

        panel.AccessoryView = accessoryView;
        var pngType = UniformTypeIdentifiers.UTType.CreateFromExtension("png");
        if (pngType != null) panel.AllowedContentTypes = [pngType];

        if (panel.RunModal() == 1 && panel.Url?.Path != null)
        {
            var exportPath = panel.Url.Path;
            var format = formatPopup.TitleOfSelectedItem;
            var quality = (float)qualitySlider.DoubleValue;
            PerformExport(exportPath, format!, quality);
        }
    }

    private string GetExportFilename()
    {
        if (_currentFilePath == null) return "export";
        return Path.GetFileNameWithoutExtension(_currentFilePath);
    }

    private static void UpdateSaveExtension(NSSavePanel panel, NSPopUpButton formatPopup)
    {
        var format = formatPopup.TitleOfSelectedItem;
        var ext = format switch
        {
            "PNG" => "png",
            "JPEG" => "jpg",
            "TIFF" => "tiff",
            "GIF" => "gif",
            _ => "png"
        };
        var utType = UniformTypeIdentifiers.UTType.CreateFromExtension(ext);
        if (utType != null) panel.AllowedContentTypes = [utType];
    }

    private void PerformExport(string path, string format, float quality)
    {
        if (_currentFilePath == null || _currentInfo == null) return;

        // GIF export is only supported for animated images
        var isAnimated = _frameDurations != null && _frameDurations.Length > 1;
        if (format == "GIF")
        {
            if (!isAnimated)
            {
                Console.WriteLine("GIF export is only supported for animated images");
                return;
            }
            ExportAnimatedGif(path);
            return;
        }

        var width = (int)_currentInfo.Size.Width;
        var height = (int)_currentInfo.Size.Height;

        // Create fresh decoder with Rgba8 format for export
        var exportOptions = JxlDecodeOptions.Default;
        exportOptions.PremultiplyAlpha = false;  // Straight alpha for PNG/TIFF
        exportOptions.PixelFormat = JxlPixelFormat.Rgba8;

        using var exportDecoder = new JxlDecoder(exportOptions);
        exportDecoder.SetInputFile(_currentFilePath);
        exportDecoder.ReadInfo();

        // Decode to Rgba8 bytes
        var pixels = new byte[width * height * 4];

        // GetPixels decodes the first frame (works for both static and animated)
        exportDecoder.GetPixels(pixels);

        // Create CGImage from sRGB RGBA8 data
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var dataProvider = new CGDataProvider(pixels);

        using var cgImage = new CGImage(
            width, height,
            8,              // bits per component
            32,             // bits per pixel
            width * 4,      // bytes per row
            colorSpace,
            CGBitmapFlags.ByteOrderDefault | CGBitmapFlags.Last, // RGBA with alpha last
            dataProvider,
            null,           // decode array
            false,          // should interpolate
            CGColorRenderingIntent.Default
        );

        // Create NSBitmapImageRep from CGImage
        using var rep = new NSBitmapImageRep(cgImage);

        var fileType = format switch
        {
            "PNG" => NSBitmapImageFileType.Png,
            "JPEG" => NSBitmapImageFileType.Jpeg,
            "TIFF" => NSBitmapImageFileType.Tiff,
            _ => NSBitmapImageFileType.Png
        };

        NSDictionary properties = format == "JPEG"
            ? NSDictionary.FromObjectAndKey(
                NSNumber.FromFloat(quality),
                new NSString("NSImageCompressionFactor"))
            : new NSDictionary();

        var outputData = rep.RepresentationUsingTypeProperties(fileType, properties);
        outputData?.Save(NSUrl.FromFilename(path), atomically: true);
    }

    private void ExportAnimatedGif(string path)
    {
        if (_currentFilePath == null || _currentInfo == null || _frameDurations == null) return;

        var width = (int)_currentInfo.Size.Width;
        var height = (int)_currentInfo.Size.Height;
        var frameCount = _frameDurations.Length;

        // Use the already-known frame durations from when the image was loaded
        var durations = _frameDurations;

        // Create CGImageDestination for animated GIF
        using var url = NSUrl.FromFilename(path);
        using var destination = CGImageDestination.Create(url, UTTypes.Gif.Identifier, frameCount);
        if (destination == null)
        {
            Console.WriteLine("Failed to create GIF destination");
            return;
        }

        // Set GIF file properties (loop count = 0 means infinite loop)
        var loopCountDict = NSDictionary.FromObjectAndKey(
            NSNumber.FromInt32(0),
            ImageIO.CGImageProperties.GIFLoopCount
        );
        var gifFileProperties = NSDictionary.FromObjectAndKey(
            loopCountDict,
            ImageIO.CGImageProperties.GIFDictionary
        );
        destination.SetProperties(gifFileProperties);

        // Create decoder for export - decode frames sequentially using streaming API
        var exportOptions = JxlDecodeOptions.Default;
        exportOptions.PremultiplyAlpha = false;
        exportOptions.PixelFormat = JxlPixelFormat.Rgba8;

        using var exportDecoder = new JxlDecoder(exportOptions);
        exportDecoder.SetInputFile(_currentFilePath);
        exportDecoder.ReadInfo();

        // Decode and add each frame using streaming API
        using var colorSpace = CGColorSpace.CreateSrgb();
        int frameIndex = 0;

        while (frameIndex < frameCount)
        {
            var evt = exportDecoder.Process();

            // Handle state transitions
            while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                evt = exportDecoder.Process();

            if (evt == JxlDecoderEvent.Complete)
                break;

            if (evt == JxlDecoderEvent.HaveFrameHeader)
                evt = exportDecoder.Process();

            if (evt == JxlDecoderEvent.NeedOutputBuffer)
            {
                // Allocate new buffer for each frame (CGDataProvider doesn't copy data)
                var pixels = new byte[width * height * 4];
                exportDecoder.ReadPixels(pixels);

                // Create CGImage for this frame
                using var dataProvider = new CGDataProvider(pixels);
                using var cgImage = new CGImage(
                    width, height,
                    8, 32, width * 4,
                    colorSpace,
                    CGBitmapFlags.ByteOrderDefault | CGBitmapFlags.Last,
                    dataProvider, null, false, CGColorRenderingIntent.Default
                );

                // Frame delay in seconds (GIF uses centiseconds internally)
                var delaySeconds = durations[frameIndex] / 1000.0;

                // Set frame properties with delay time
                var delayDict = NSDictionary.FromObjectAndKey(
                    NSNumber.FromFloat((float)delaySeconds),
                    ImageIO.CGImageProperties.GIFDelayTime
                );
                var frameProperties = NSDictionary.FromObjectAndKey(
                    delayDict,
                    ImageIO.CGImageProperties.GIFDictionary
                );

                destination.AddImage(cgImage, frameProperties);
                frameIndex++;
            }
        }

        // Finalize the GIF
        if (!destination.Close())
        {
            Console.WriteLine("Failed to finalize GIF");
        }
    }

    private void PerformCommandLineExport()
    {
        var exportPath = Program.Args.ExportFile!;
        var format = Program.Args.ExportFormat!;

        // GIF export is only supported for animated images
        var isAnimated = _frameDurations != null && _frameDurations.Length > 1;
        if (format == "GIF" && !isAnimated)
        {
            Console.Error.WriteLine("Export failed: GIF export is only supported for animated images");
            NSApplication.SharedApplication.Terminate(null);
            return;
        }

        // Seek to specified frame if provided (frame export is TODO)
        if (Program.Args.ExportFrameIndex.HasValue && _frameDurations != null)
        {
            var frameIndex = Math.Clamp(Program.Args.ExportFrameIndex.Value, 0, _frameDurations.Length - 1);
            _currentFrameIndex = frameIndex;
        }

        try
        {
            PerformExport(exportPath, format, 0.85f);
            NSApplication.SharedApplication.Terminate(null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export failed: {ex.Message}");
            NSApplication.SharedApplication.Terminate(null);
        }
    }

    public async void LoadImage(string path)
    {
        try
        {
            Console.WriteLine($"[LoadImage] Path: {path}");
            Console.WriteLine($"[LoadImage] File exists: {File.Exists(path)}");
            Console.WriteLine($"[LoadImage] Current directory: {Environment.CurrentDirectory}");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"File not found: {path}");
            }

            _currentFilePath = path;
            _currentFileModified = File.GetLastWriteTimeUtc(path);
            StopAnimation();
            var filename = Path.GetFileName(path);
            Subtitle = $"Loading {filename}";
            _hdrLabel!.Hidden = true;
            _frameLabel!.Hidden = true;

            Console.WriteLine($"[LoadImage] Loading image: {path}");

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

            // Get color profile description and detect transfer function
            string colorProfileDesc;
            bool isHlg = false;
            bool isPq = false;
            IccHeaderInfo? iccHeader = null;
            IccColorSpaceInfo? iccColorSpace = null;
            using (var profile = decoder.GetEmbeddedColorProfile())
            {
                Console.WriteLine($"[ColorProfile] IsIcc={profile.IsIcc}");
                if (profile.IsIcc)
                {
                    // Parse ICC profile header and color space info
                    iccHeader = IccProfileParser.TryGetHeaderInfo(profile.IccData);
                    iccColorSpace = IccProfileParser.TryGetColorSpaceInfo(profile.IccData);

                    if (iccColorSpace.HasValue)
                    {
                        isHlg = iccColorSpace.Value.IsHlg;
                        isPq = iccColorSpace.Value.IsPq;
                        // Use color space description (e.g., "Rec.2100 HLG") if available
                        colorProfileDesc = $"ICC: {iccColorSpace.Value}";
                        Console.WriteLine($"[ColorProfile] ICC parsed: {iccColorSpace.Value}, IsHlg={isHlg}, IsPq={isPq}");
                    }
                    else
                    {
                        // Fallback to profile description
                        var iccName = IccProfileParser.TryGetDescription(profile.IccData);
                        colorProfileDesc = iccName != null ? $"ICC: {iccName}" : "ICC";
                        Console.WriteLine($"[ColorProfile] ICC name from parser: '{iccName}'");
                    }
                }
                else
                {
                    colorProfileDesc = profile.GetDescription();
                    Console.WriteLine($"[ColorProfile] GetDescription returned: '{colorProfileDesc}'");
                    isHlg = profile.IsHlg;
                    isPq = profile.IsPq;
                    Console.WriteLine($"[ColorProfile] IsHlg={isHlg}, IsPq={isPq}");
                }
            }

            Console.WriteLine($"[ColorProfile] Final desc before check: '{colorProfileDesc}'");
            if (string.IsNullOrWhiteSpace(colorProfileDesc))
            {
                colorProfileDesc = "(no color profile)";
            }
            Console.WriteLine($"[ColorProfile] Final desc after check: '{colorProfileDesc}'");

            var isHdr = info.IsHdr;

            // For HLG/PQ content, set output color profile to keep native encoding
            // The system tone mapper (via CAEdrMetadata) expects HLG/PQ encoded values
            if (isHlg || isPq)
            {
                // Create output profile that keeps the native HLG/PQ transfer function
                // Use Bt2100 primaries (Rec.2020) which is standard for HDR content
                using var outputProfile = JxlColorProfile.FromEncoding(
                    JxlProfileType.Rgb,
                    whitePoint: JxlWhitePointType.D65,
                    primaries: JxlPrimariesType.Bt2100,
                    transferFunction: isHlg ? JxlTransferFunctionType.Hlg : JxlTransferFunctionType.Pq);
                decoder.SetOutputColorProfile(outputProfile);
                Console.WriteLine($"[ColorProfile] Set output to {(isHlg ? "HLG" : "PQ")} Rec.2100 for system tone mapping");
            }

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

            // Cache metadata for clipboard copy
            _metadata = new JxlImageMetadata
            {
                FilePath = path,
                BasicInfo = info,
                ColorProfileDescription = colorProfileDesc,
                IsHlg = isHlg,
                IsPq = isPq,
                FrameCount = _frameDurations?.Length ?? 1,
                IccHeader = iccHeader,
                IccColorSpace = iccColorSpace
            };

            // Set status bar labels
            _dimensionsLabel!.StringValue = $"{info.Size.Width}×{info.Size.Height}";
            _profileLabel!.StringValue = colorProfileDesc;

            // Configure Metal view for the content type
            _hdrLabel!.Hidden = !(isHdr || isHlg || isPq);

            if (isHlg)
            {
                // HLG: Use system tone mapping via CAEdrMetadata
                _metalView!.ConfigureForHlg();
            }
            else if (isPq)
            {
                // PQ: Use system tone mapping via CAEdrMetadata
                _metalView!.ConfigureForPq(info.ToneMapping.IntensityTarget);
            }
            else if (isHdr)
            {
                // Other HDR (rare): Use linear color space with manual brightness scaling
                _metalView!.ConfigureForLinear();
            }
            else
            {
                // SDR: Use sRGB color space for standard gamma-encoded content
                _metalView!.ConfigureForSrgb();
                _metalView.HdrBrightnessScale = 1.0f;
            }

            // Update HDR label with current screen's EDR headroom
            UpdateHdrLabel();

            Subtitle = filename;
            _metalView.ResetView();  // Display at 1:1 pixel ratio
            UpdateStatus();  // Show zoom level
            UpdatePlayPauseButton();

            // Handle command-line export if requested
            if (Program.Args.ExportFile != null && Program.Args.ExportFormat != null)
            {
                PerformCommandLineExport();
            }
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

    private string FormatMetadata()
    {
        if (_metadata == null) return string.Empty;

        var info = _metadata.BasicInfo;
        var sb = new StringBuilder();

        sb.AppendLine($"File: {_metadata.FilePath}");
        sb.AppendLine($"Dimensions: {info.Size.Width} x {info.Size.Height}");

        // Bit depth
        if (info.BitDepth.IsFloat)
        {
            sb.AppendLine($"Bit Depth: {info.BitDepth.BitsPerSample}-bit float ({info.BitDepth.ExponentBitsPerSample} exponent bits)");
        }
        else
        {
            sb.AppendLine($"Bit Depth: {info.BitDepth.BitsPerSample}-bit integer");
        }

        // Color profile
        sb.AppendLine($"Color Profile: {_metadata.ColorProfileDescription}");

        // ICC profile details (if available)
        if (_metadata.IccHeader.HasValue)
        {
            var header = _metadata.IccHeader.Value;
            sb.AppendLine($"  ICC Version: {header.IccVersion.Major}.{header.IccVersion.Minor}");
            sb.AppendLine($"  Profile Class: {header.ProfileClass}");
            sb.AppendLine($"  Color Space: {header.ColorSpace}");
            sb.AppendLine($"  Rendering Intent: {header.RenderingIntent}");
        }

        if (_metadata.IccColorSpace.HasValue)
        {
            var cs = _metadata.IccColorSpace.Value;
            if (cs.WhitePoint.HasValue)
            {
                var wp = cs.WhitePoint.Value;
                var wpName = cs.IsD65WhitePoint ? " (D65)" : cs.IsD50WhitePoint ? " (D50)" : "";
                sb.AppendLine($"  White Point: ({wp.X:F4}, {wp.Y:F4}, {wp.Z:F4}){wpName}");
            }
            if (cs.TransferFunction.HasValue)
            {
                var tf = cs.TransferFunction.Value;
                var tfStr = tf.ToString();
                if (tf == IccTransferFunction.Gamma && cs.GammaValue.HasValue)
                {
                    tfStr = $"Gamma {cs.GammaValue.Value:F2}";
                }
                sb.AppendLine($"  Transfer Function: {tfStr}");
            }
            if (cs.CicpPrimaries.HasValue && cs.CicpPrimaries != IccCicpPrimaries.Unknown)
            {
                sb.AppendLine($"  CICP Primaries: {cs.CicpPrimaries}");
            }
            if (cs.CicpTransfer.HasValue && cs.CicpTransfer != IccCicpTransfer.Unknown)
            {
                sb.AppendLine($"  CICP Transfer: {cs.CicpTransfer}");
            }
        }

        // HDR info
        if (_metadata.IsHlg)
        {
            sb.AppendLine($"HDR: HLG ({info.ToneMapping.IntensityTarget:F0} nits)");
        }
        else if (_metadata.IsPq)
        {
            sb.AppendLine($"HDR: PQ ({info.ToneMapping.IntensityTarget:F0} nits)");
        }
        else if (info.IsHdr)
        {
            sb.AppendLine($"HDR: Yes ({info.ToneMapping.IntensityTarget:F0} nits)");
        }
        else
        {
            sb.AppendLine("HDR: No");
        }

        // Animation
        if (info.IsAnimated && info.Animation.HasValue)
        {
            var anim = info.Animation.Value;
            var fps = (double)anim.TpsNumerator / anim.TpsDenominator;
            sb.AppendLine($"Animation: {_metadata.FrameCount} frames, {fps:F2} fps");
            if (anim.NumLoops != 0)
            {
                sb.AppendLine($"Loops: {anim.NumLoops}");
            }
        }

        // Alpha
        sb.AppendLine($"Has Alpha: {(info.HasAlpha ? "Yes" : "No")}");
        if (info.AlphaPremultiplied)
        {
            sb.AppendLine("Alpha Premultiplied: Yes");
        }

        // Orientation
        if (info.Orientation != JxlOrientation.Identity)
        {
            sb.AppendLine($"Orientation: {info.Orientation}");
        }

        // Extra channels (beyond alpha)
        var extraChannels = info.ExtraChannels
            .Where(ec => ec.ChannelType != JxlExtraChannelType.Alpha)
            .ToList();
        if (extraChannels.Count > 0)
        {
            sb.AppendLine($"Extra Channels: {string.Join(", ", extraChannels.Select(ec => ec.ChannelType))}");
        }

        return sb.ToString().TrimEnd();
    }

    private void CopyMetadata()
    {
        var metadata = FormatMetadata();
        if (string.IsNullOrEmpty(metadata)) return;

        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetStringForType(metadata, NSPasteboardType.String.GetConstant()!);
    }

    private static void ShowAlert(string title, string message)
    {
        var alert = new NSAlert
        {
            AlertStyle = NSAlertStyle.Warning,
            MessageText = title,
            InformativeText = message
        };
        alert.AddButton("OK");
        alert.RunModal();
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

    /// <summary>
    /// NSTextField subclass that stores metadata getter for use by the custom field editor.
    /// </summary>
    private sealed class SelectableLabel : NSTextField
    {
        public Func<string>? GetMetadata { get; }

        public SelectableLabel(Func<string> getMetadata)
        {
            GetMetadata = getMetadata;
        }
    }

    // Custom field editor for status bar labels
    private NSTextView? _metadataFieldEditor;

    public override NSText FieldEditor(bool createWhenNeeded, NSObject? forObject)
    {
        if (forObject is SelectableLabel selectableLabel)
        {
            if (_metadataFieldEditor == null && createWhenNeeded)
            {
                _metadataFieldEditor = new NSTextView
                {
                    FieldEditor = true
                };
            }

            if (_metadataFieldEditor != null)
            {

                // Build custom menu with Copy Metadata
                var menu = new NSMenu();
                menu.AddItem(new NSMenuItem("Cut", new ObjCRuntime.Selector("cut:"), "x"));
                menu.AddItem(new NSMenuItem("Copy", new ObjCRuntime.Selector("copy:"), "c"));
                menu.AddItem(new NSMenuItem("Paste", new ObjCRuntime.Selector("paste:"), "v"));
                menu.AddItem(NSMenuItem.SeparatorItem);
                menu.AddItem(new NSMenuItem("Copy Metadata", (s, e) => CopyMetadata()));

                _metadataFieldEditor.Menu = menu;
                return _metadataFieldEditor;
            }
        }

        return base.FieldEditor(createWhenNeeded, forObject);
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
                    item.Label = "File";
                    item.ToolTip = "File operations";

                    // Create pull-down popup button
                    var popup = new NSPopUpButton(new CGRect(0, 0, 80, 24), pullsDown: true);
                    ((NSPopUpButtonCell)popup.Cell).ArrowPosition = NSPopUpArrowPosition.None;

                    // First item is the button title (shown when closed)
                    popup.AddItem("File ⌵");
                    popup.AddItem("Open...");
                    popup.AddItem("Export...");

                    // Handle selection
                    var window = _window;  // Capture for closure
                    popup.Activated += (s, e) =>
                    {
                        var selectedIndex = popup.IndexOfSelectedItem;
                        switch (selectedIndex)
                        {
                            case 1: window.OpenFile(); break;
                            case 2: window.ExportImage(); break;
                        }
                        popup.SelectItem(0);  // Reset to show "File"
                    };

                    item.View = popup;
                    item.Navigational = true;
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
