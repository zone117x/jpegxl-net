using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppKit;
using CoreGraphics;
using Foundation;
using ImageIO;
using JpegXL.Net;
using Metal;
using UniformTypeIdentifiers;

namespace JpegXL.MacOS;

public enum ImageFormat { Png, Jpeg, Tiff, Gif }

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
    private NSButton? _hdrLabel;
    private NSTextField? _frameLabel;
    private NSButton? _playPauseButton;
    private NSButton? _fitButton;
    private NSMenuItem? _hdrSdrMenuItem;
    private NSMenuItem? _hdrSdrMenuBarItem;
    private NSMenuItem? _comparisonMenuBarItem;

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

    // HDR/SDR display mode toggle
    private bool _displayAsSdr;

    // HDR vs SDR comparison mode
    private bool _comparisonMode;
    private HdrMetalView? _sdrMetalView;
    private ComparisonDividerView? _comparisonDivider;
    private nfloat _dividerPosition = 0.5f;
    private bool _wasPlayingBeforeComparison;
    private NSMenuItem? _comparisonMenuItem;
    private NSTextField? _hdrSideLabel;
    private NSTextField? _sdrSideLabel;

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
        CreateMainMenu();

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
        _sdrMetalView?.UpdateContentsScale();
        UpdateHdrLabel();
        if (_comparisonMode) UpdateComparisonLayout();
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

        string title;
        NSColor color;

        if (_displayAsSdr)
        {
            var source = _metadata.IsHlg ? "HLG" : _metadata.IsPq ? "PQ" : "HDR";
            title = $"SDR (from {source})";
            color = NSColor.SystemBlue;
            Console.WriteLine($"[HDR] Displaying as tone-mapped SDR (source: {source})");
        }
        else if (_metadata.IsHlg)
        {
            title = $"HDR HLG: {intensityTarget:F0} nits";
            color = NSColor.Orange;
            if (edrHeadroom > 1.0) title += $" | EDR: {edrHeadroom:F1}x";
            Console.WriteLine($"[HDR] HLG system tone mapping (EDR headroom: {edrHeadroom:F1}x)");
        }
        else if (_metadata.IsPq)
        {
            title = $"HDR PQ: {intensityTarget:F0} nits";
            color = NSColor.Orange;
            if (edrHeadroom > 1.0) title += $" | EDR: {edrHeadroom:F1}x";
            Console.WriteLine($"[HDR] PQ system tone mapping (max: {intensityTarget} nits, EDR headroom: {edrHeadroom:F1}x)");
        }
        else if (_metadata.BasicInfo.IsHdr)
        {
            title = $"HDR: {intensityTarget:F0} nits";
            color = NSColor.Orange;
            if (edrHeadroom > 1.0) title += $" | EDR: {edrHeadroom:F1}x";

            // Manual HDR mode needs brightness scale recalculation
            // 203 nits is the SDR reference white level defined in ITU-R BT.2408
            const float SdrReferenceWhiteNits = 203f;
            var idealScale = intensityTarget / SdrReferenceWhiteNits;
            var brightnessScale = Math.Min(idealScale, edrHeadroom);
            _metalView!.HdrBrightnessScale = brightnessScale;
            Console.WriteLine($"[HDR] Linear mode with brightness scale: {brightnessScale:F2}x (EDR headroom: {edrHeadroom:F1}x)");
        }
        else
        {
            return;
        }

        // Apply colored title to the button using attributed string
        var attrs = new NSDictionary(
            NSStringAttributeKey.ForegroundColor, color,
            NSStringAttributeKey.Font, NSFont.SystemFontOfSize(12)!);
        _hdrLabel!.AttributedTitle = new NSAttributedString(title, attrs);
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
            OnZoomChanged = _ => { UpdateStatus(); UpdateFitButton(); }
        };

        // Context menu for right-click
        var contextMenu = new NSMenu();
        contextMenu.AddItem(new NSMenuItem("Copy Metadata", (s, e) => CopyMetadata()));
        contextMenu.AddItem(new NSMenuItem("Copy Image", (s, e) => CopyImageToClipboard()));
        contextMenu.AddItem(CreateCopyAsMenuItem());
        contextMenu.AddItem(NSMenuItem.SeparatorItem);
        _hdrSdrMenuItem = new NSMenuItem("Display as SDR", (s, e) => ToggleHdrSdr());
        _hdrSdrMenuItem.Hidden = true;
        contextMenu.AddItem(_hdrSdrMenuItem);
        _comparisonMenuItem = new NSMenuItem("Compare HDR vs SDR", (s, e) => ToggleComparisonMode());
        _comparisonMenuItem.Hidden = true;
        contextMenu.AddItem(_comparisonMenuItem);
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

        // HDR label button (content-hugging, initially hidden, shows popup menu on click)
        _hdrLabel = new NSButton
        {
            Title = "",
            BezelStyle = NSBezelStyle.Recessed,
            Bordered = true,
            Font = NSFont.SystemFontOfSize(12)!,
            Hidden = true,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        _hdrLabel.SetButtonType(NSButtonType.MomentaryPushIn);
        _hdrLabel.SetContentHuggingPriorityForOrientation(750f, NSLayoutConstraintOrientation.Horizontal);
        _hdrLabel.SetContentCompressionResistancePriority(750f, NSLayoutConstraintOrientation.Horizontal);
        _hdrLabel.Activated += (s, e) => ShowHdrMenu();
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

    private void CreateMainMenu()
    {
        var mainMenu = new NSMenu();

        // App menu
        var appMenuItem = new NSMenuItem();
        mainMenu.AddItem(appMenuItem);
        var appMenu = new NSMenu();
        appMenu.AddItem(new NSMenuItem("About JPEG XL Viewer",
            new ObjCRuntime.Selector("orderFrontStandardAboutPanel:"), ""));
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        appMenu.AddItem(new NSMenuItem("Quit JPEG XL Viewer",
            new ObjCRuntime.Selector("terminate:"), "q"));
        appMenuItem.Submenu = appMenu;

        // File menu
        var fileMenuItem = new NSMenuItem();
        mainMenu.AddItem(fileMenuItem);
        var fileMenu = new NSMenu("File");
        fileMenu.AddItem(new NSMenuItem("Open...", (s, e) =>
            NSApplication.SharedApplication.BeginInvokeOnMainThread(OpenFile)) { KeyEquivalent = "o" });
        fileMenu.AddItem(new NSMenuItem("Export...", (s, e) =>
            NSApplication.SharedApplication.BeginInvokeOnMainThread(ExportImage)) { KeyEquivalent = "e" });
        fileMenu.AddItem(NSMenuItem.SeparatorItem);
        fileMenu.AddItem(new NSMenuItem("Close Window",
            new ObjCRuntime.Selector("performClose:"), "w"));
        fileMenuItem.Submenu = fileMenu;

        // Edit menu
        var editMenuItem = new NSMenuItem();
        mainMenu.AddItem(editMenuItem);
        var editMenu = new NSMenu("Edit");
        editMenu.AddItem(new NSMenuItem("Copy Image", (s, e) => CopyImageToClipboard())
            { KeyEquivalent = "c" });
        editMenu.AddItem(CreateCopyAsMenuItem());
        var copyMetadataItem = new NSMenuItem("Copy Metadata", (s, e) => CopyMetadata())
            { KeyEquivalent = "c" };
        copyMetadataItem.KeyEquivalentModifierMask =
            NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask;
        editMenu.AddItem(copyMetadataItem);
        editMenuItem.Submenu = editMenu;

        // View menu
        var viewMenuItem = new NSMenuItem();
        mainMenu.AddItem(viewMenuItem);
        var viewMenu = new NSMenu("View");
        viewMenu.AddItem(new NSMenuItem("Zoom In", (s, e) => ZoomIn()) { KeyEquivalent = "=" });
        viewMenu.AddItem(new NSMenuItem("Zoom Out", (s, e) => ZoomOut()) { KeyEquivalent = "-" });
        viewMenu.AddItem(new NSMenuItem("Actual Size", (s, e) => ActualSize()) { KeyEquivalent = "1" });
        viewMenu.AddItem(new NSMenuItem("Fit to Window", (s, e) => ToggleFitMode()) { KeyEquivalent = "0" });
        viewMenu.AddItem(NSMenuItem.SeparatorItem);
        _hdrSdrMenuBarItem = new NSMenuItem("Display as SDR", (s, e) => ToggleHdrSdr());
        _hdrSdrMenuBarItem.Enabled = false;
        viewMenu.AddItem(_hdrSdrMenuBarItem);
        _comparisonMenuBarItem = new NSMenuItem("Compare HDR vs SDR", (s, e) => ToggleComparisonMode());
        _comparisonMenuBarItem.Enabled = false;
        viewMenu.AddItem(_comparisonMenuBarItem);
        viewMenuItem.Submenu = viewMenu;

        NSApplication.SharedApplication.MainMenu = mainMenu;
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

        panel.BeginSheet(this, result =>
        {
            if (result == 1 && panel.Url?.Path != null)
            {
                LoadImage(panel.Url.Path);
            }
        });
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

    private void ToggleFitMode()
    {
        if (_metalView == null) return;
        _metalView.FitMode = _fitButton?.State == NSCellStateValue.On;
        if (_metalView.FitMode)
            _metalView.ZoomToFit();
        UpdateStatus();
    }

    private void UpdateFitButton()
    {
        if (_fitButton == null || _metalView == null) return;
        _fitButton.State = _metalView.FitMode ? NSCellStateValue.On : NSCellStateValue.Off;
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

        var panel = NSSavePanel.SavePanel;
        panel.Title = "Export Image";
        panel.NameFieldStringValue = GetExportFilename();

        // Create accessory view with format selector, tone mapping, and quality slider
        var isHdr = _metadata is { IsHlg: true } or { IsPq: true };
        var accessoryHeight = isHdr ? 110 : 80;
        var accessoryView = new NSView(new CGRect(0, 0, 280, accessoryHeight));

        // Format row (shifts up when tone mapping row is visible)
        var formatY = isHdr ? 82 : 52;
        var formatLabel = new NSTextField(new CGRect(0, formatY, 60, 20))
        {
            StringValue = "Format:",
            Editable = false,
            Bordered = false,
            DrawsBackground = false
        };
        accessoryView.AddSubview(formatLabel);

        var formatPopup = new NSPopUpButton(new CGRect(65, formatY - 4, 120, 26), pullsDown: false);
        var exportFormats = new (string Title, ImageFormat Format)[] {
            ("PNG", ImageFormat.Png), ("JPEG", ImageFormat.Jpeg), ("TIFF", ImageFormat.Tiff)
        };
        foreach (var (title, fmt) in exportFormats)
            formatPopup.Menu!.AddItem(new NSMenuItem(title) { Tag = (nint)fmt });
        if (isAnimated)
            formatPopup.Menu!.AddItem(new NSMenuItem("GIF") { Tag = (nint)ImageFormat.Gif });
        formatPopup.SelectItem(0);
        accessoryView.AddSubview(formatPopup);

        // Tone mapping row (HDR images only)
        NSPopUpButton? toneMappingPopup = null;
        if (isHdr)
        {
            var toneMapLabel = new NSTextField(new CGRect(0, 52, 70, 20))
            {
                StringValue = "Tone Map:",
                Editable = false,
                Bordered = false,
                DrawsBackground = false
            };
            accessoryView.AddSubview(toneMapLabel);

            toneMappingPopup = new NSPopUpButton(new CGRect(75, 48, 190, 26), pullsDown: false);
            foreach (var (title, method) in new[] {
                ("BT.2446a Perceptual", JxlToneMappingMethod.Bt2446aPerceptual),
                ("BT.2446a", JxlToneMappingMethod.Bt2446a),
                ("BT.2446a Linear", JxlToneMappingMethod.Bt2446aLinear),
            })
            {
                var item = new NSMenuItem(title) { Tag = (nint)method };
                toneMappingPopup.Menu!.AddItem(item);
            }
            toneMappingPopup.SelectItem(0);
            accessoryView.AddSubview(toneMappingPopup);
        }

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
            var isJpeg = (ImageFormat)(int)formatPopup.SelectedItem.Tag == ImageFormat.Jpeg;
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
        UpdateSaveExtension(panel, formatPopup);

        panel.BeginSheet(this, result =>
        {
            if (result == 1 && panel.Url?.Path != null)
            {
                var exportPath = panel.Url.Path;
                var fmt = (ImageFormat)(int)formatPopup.SelectedItem.Tag;
                var quality = (float)qualitySlider.DoubleValue;
                var toneMapping = toneMappingPopup?.SelectedItem is { } selectedItem
                    ? (JxlToneMappingMethod)(int)selectedItem.Tag
                    : JxlToneMappingMethod.None;
                var filePath = _currentFilePath!;
                var info = _currentInfo!;

                if (fmt == ImageFormat.Gif)
                {
                    // GIF uses CGImageDestination (ImageIO) — safe on background thread
                    RunWithSpinner(
                        () => ExportAnimatedGif(filePath, info, _frameDurations, exportPath, toneMapping),
                        message: "Exporting...");
                }
                else
                {
                    // Decode on background thread, NSBitmapImageRep on main thread
                    var width = (int)info.Size.Width;
                    var height = (int)info.Size.Height;
                    RunWithSpinner(
                        () =>
                        {
                            using var decoder = CreateSrgbDecoder(filePath, toneMapping);
                            var pixels = new byte[width * height * 4];
                            decoder.GetPixels(pixels);
                            return pixels;
                        },
                        pixels => SaveImageFile(pixels, width, height, exportPath, fmt, quality),
                        "Exporting...");
                }
            }
        });
    }

    private string GetExportFilename()
    {
        if (_currentFilePath == null) return "export";
        return Path.GetFileNameWithoutExtension(_currentFilePath);
    }

    private static void UpdateSaveExtension(NSSavePanel panel, NSPopUpButton formatPopup)
    {
        var format = (ImageFormat)(int)formatPopup.SelectedItem.Tag;
        var ext = format switch
        {
            ImageFormat.Png => "png",
            ImageFormat.Jpeg => "jpg",
            ImageFormat.Tiff => "tiff",
            ImageFormat.Gif => "gif",
            _ => "png"
        };
        var utType = UniformTypeIdentifiers.UTType.CreateFromExtension(ext);
        if (utType != null) panel.AllowedContentTypes = [utType];
    }

    /// <summary>
    /// Creates a JxlDecoder configured for sRGB Rgba8 output with optional tone mapping.
    /// Caller is responsible for disposing the returned decoder.
    /// </summary>
    private static JxlDecoder CreateSrgbDecoder(string filePath, JxlToneMappingMethod toneMapping)
    {
        var options = JxlDecodeOptions.Default;
        options.PremultiplyAlpha = false;
        options.PixelFormat = JxlPixelFormat.Rgba8;
        if (toneMapping != JxlToneMappingMethod.None)
            options.ToneMappingMethod = toneMapping;

        var decoder = new JxlDecoder(options);
        decoder.SetInputFile(filePath);
        decoder.ReadInfo();

        if (toneMapping != JxlToneMappingMethod.None)
        {
            using var srgbProfile = JxlColorProfile.FromEncoding(
                JxlProfileType.Rgb,
                whitePoint: JxlWhitePointType.D65,
                primaries: JxlPrimariesType.Srgb,
                transferFunction: JxlTransferFunctionType.Srgb);
            decoder.SetOutputColorProfile(srgbProfile);
        }

        return decoder;
    }

    private void PerformExport(string path, ImageFormat format, float quality, JxlToneMappingMethod toneMapping)
    {
        if (_currentFilePath == null || _currentInfo == null) return;

        if (format == ImageFormat.Gif)
        {
            ExportAnimatedGif(_currentFilePath, _currentInfo, _frameDurations, path, toneMapping);
            return;
        }

        var width = (int)_currentInfo.Size.Width;
        var height = (int)_currentInfo.Size.Height;

        using var exportDecoder = CreateSrgbDecoder(_currentFilePath, toneMapping);

        // Decode to Rgba8 bytes
        var pixels = new byte[width * height * 4];
        exportDecoder.GetPixels(pixels);

        SaveImageFile(pixels, width, height, path, format, quality);
    }

    /// <summary>
    /// Encodes RGBA8 pixel data to image data. Must be called on the main thread (uses NSBitmapImageRep).
    /// </summary>
    private static NSData? EncodePixels(byte[] pixels, int width, int height, ImageFormat format, float quality = 0f)
    {
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var dataProvider = new CGDataProvider(pixels);
        using var cgImage = new CGImage(
            width, height,
            8, 32, width * 4,
            colorSpace,
            CGBitmapFlags.ByteOrderDefault | CGBitmapFlags.Last,
            dataProvider, null, false, CGColorRenderingIntent.Default
        );

        using var rep = new NSBitmapImageRep(cgImage);

        var fileType = format switch
        {
            ImageFormat.Png => NSBitmapImageFileType.Png,
            ImageFormat.Jpeg => NSBitmapImageFileType.Jpeg,
            ImageFormat.Tiff => NSBitmapImageFileType.Tiff,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported image format")
        };

        NSDictionary properties = format == ImageFormat.Jpeg
            ? NSDictionary.FromObjectAndKey(
                NSNumber.FromFloat(quality),
                new NSString("NSImageCompressionFactor"))
            : new NSDictionary();

        return rep.RepresentationUsingTypeProperties(fileType, properties);
    }

    private static void SaveImageFile(byte[] pixels, int width, int height, string path, ImageFormat format, float quality)
    {
        var data = EncodePixels(pixels, width, height, format, quality);
        data?.Save(NSUrl.FromFilename(path), atomically: true);
    }

    /// <summary>
    /// Encodes all frames to animated GIF data. Uses only CoreGraphics/ImageIO — safe on background thread.
    /// </summary>
    private static NSMutableData? EncodeAnimatedGif(string filePath, JxlBasicInfo info, float[] frameDurations, JxlToneMappingMethod toneMapping)
    {
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;
        var frameCount = frameDurations.Length;

        var gifData = new NSMutableData();
        using var destination = CGImageDestination.Create(gifData, UTTypes.Gif.Identifier, frameCount);
        if (destination == null)
        {
            Console.Error.WriteLine("Failed to create GIF destination");
            return null;
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

        using var exportDecoder = CreateSrgbDecoder(filePath, toneMapping);

        using var colorSpace = CGColorSpace.CreateSrgb();
        int frameIndex = 0;

        while (frameIndex < frameCount)
        {
            var evt = exportDecoder.Process();

            while (evt == JxlDecoderEvent.NeedMoreInput || evt == JxlDecoderEvent.HaveBasicInfo)
                evt = exportDecoder.Process();

            if (evt == JxlDecoderEvent.Complete)
                break;

            if (evt == JxlDecoderEvent.HaveFrameHeader)
                evt = exportDecoder.Process();

            if (evt == JxlDecoderEvent.NeedOutputBuffer)
            {
                var pixels = new byte[width * height * 4];
                exportDecoder.ReadPixels(pixels);

                using var dataProvider = new CGDataProvider(pixels);
                using var cgImage = new CGImage(
                    width, height,
                    8, 32, width * 4,
                    colorSpace,
                    CGBitmapFlags.ByteOrderDefault | CGBitmapFlags.Last,
                    dataProvider, null, false, CGColorRenderingIntent.Default
                );

                var delaySeconds = frameDurations[frameIndex] / 1000.0;
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

        if (!destination.Close())
        {
            Console.Error.WriteLine("Failed to finalize GIF");
            return null;
        }

        return gifData;
    }

    private static void ExportAnimatedGif(string filePath, JxlBasicInfo info, float[]? frameDurations, string path, JxlToneMappingMethod toneMapping)
    {
        if (frameDurations == null) return;
        var gifData = EncodeAnimatedGif(filePath, info, frameDurations, toneMapping);
        gifData?.Save(NSUrl.FromFilename(path), atomically: true);
    }

    private void PerformCommandLineExport()
    {
        var exportPath = Program.Args.ExportFile!;
        var format = Program.Args.ExportFormat!.Value;

        // GIF export is only supported for animated images
        var isAnimated = _frameDurations != null && _frameDurations.Length > 1;
        if (format == ImageFormat.Gif && !isAnimated)
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
            var isHdr = _metadata is { IsHlg: true } or { IsPq: true };
            PerformExport(exportPath, format, 0.85f, isHdr ? JxlToneMappingMethod.Bt2446aPerceptual : JxlToneMappingMethod.None);
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
            _displayAsSdr = false;  // Reset to HDR mode for new images
            ExitComparisonMode();
            StopAnimation();
            var filename = Path.GetFileName(path);
            Subtitle = $"Loading {filename}";
            _hdrLabel!.Hidden = true;
            _hdrSdrMenuItem!.Hidden = true;
            _frameLabel!.Hidden = true;

            Console.WriteLine($"[LoadImage] Loading image: {path}");

            var options = JxlDecodeOptions.Default;
            options.PremultiplyAlpha = true;
            options.PixelFormat = JxlPixelFormat.Rgba32F;

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
                using var profile = JxlColorProfile.FromEncoding(
                    JxlProfileType.Rgb,
                    whitePoint: JxlWhitePointType.D65,
                    primaries: JxlPrimariesType.Bt2100,
                    transferFunction: isHlg ? JxlTransferFunctionType.Hlg : JxlTransferFunctionType.Pq);
                decoder.SetOutputColorProfile(profile);
                Console.WriteLine($"[ColorProfile] Set output to {(isHlg ? "HLG" : "PQ")} Rec.2100 for system tone mapping");
            }
            else
            {
                // SDR/other: explicit linear sRGB output for consistent Metal linear color space rendering
                using var profile = JxlColorProfile.CreateLinearSrgb();
                decoder.SetOutputColorProfile(profile);
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
                _metalView!.ConfigureForPq(info.ToneMapping.IntensityTarget, info.ToneMapping.MinNits);
            }
            else if (isHdr)
            {
                // Other HDR (rare): Use linear color space with manual brightness scaling
                _metalView!.ConfigureForLinear();
            }
            else
            {
                // SDR: Use linear sRGB color space to match decoder's linear float output
                _metalView!.ConfigureForLinearSrgb();
                _metalView.HdrBrightnessScale = 1.0f;
            }

            // Update HDR label with current screen's EDR headroom
            UpdateHdrLabel();
            UpdateHdrToggle();

            Subtitle = filename;
            _metalView.FitMode = true;
            _metalView.ZoomToFit();
            UpdateFitButton();
            UpdateStatus();
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
            _hdrSdrMenuItem!.Hidden = true;
        }
    }

    /// <summary>
    /// Toggles between HDR and tone-mapped SDR display for the current image.
    /// </summary>
    private void ToggleHdrSdr()
    {
        if (_currentFilePath == null || _metadata == null) return;
        if (!(_metadata.IsHlg || _metadata.IsPq || _metadata.BasicInfo.IsHdr)) return;

        _displayAsSdr = !_displayAsSdr;
        ReloadWithDisplayMode();
    }

    /// <summary>
    /// Reloads the current image with the active display mode (HDR or tone-mapped SDR).
    /// </summary>
    private async void ReloadWithDisplayMode()
    {
        if (_currentFilePath == null || _metadata == null || _currentInfo == null) return;

        try
        {
            var info = _currentInfo;
            var isHlg = _metadata.IsHlg;
            var isPq = _metadata.IsPq;

            Console.WriteLine($"[DisplayMode] Reloading as {(_displayAsSdr ? "SDR" : "HDR")}");

            var options = JxlDecodeOptions.Default;
            options.PremultiplyAlpha = true;
            options.PixelFormat = JxlPixelFormat.Rgba32F;
            // BT.2446a tone mapping for SDR mode (default 203 cd/m² = ITU-R BT.2408 SDR reference white)
            options.ToneMappingMethod = _displayAsSdr ? JxlToneMappingMethod.Bt2446aPerceptual : JxlToneMappingMethod.None;

            using var decoder = new JxlDecoder(options);
            await decoder.SetInputFileAsync(_currentFilePath);
            decoder.ReadInfo();

            if (!_displayAsSdr && (isHlg || isPq))
            {
                // HDR mode: keep native HLG/PQ encoding for system tone mapper
                using var profile = JxlColorProfile.FromEncoding(
                    JxlProfileType.Rgb,
                    whitePoint: JxlWhitePointType.D65,
                    primaries: JxlPrimariesType.Bt2100,
                    transferFunction: isHlg ? JxlTransferFunctionType.Hlg : JxlTransferFunctionType.Pq);
                decoder.SetOutputColorProfile(profile);
            }
            else if (_displayAsSdr && (isHlg || isPq))
            {
                // SDR mode: tone-mapped output to linear sRGB (same as regular SDR images)
                using var profile = JxlColorProfile.CreateLinearSrgb();
                decoder.SetOutputColorProfile(profile);
            }

            StopAnimation();
            _frameDurations = null;

            if (info.IsAnimated)
            {
                var animationMetadata = decoder.ParseFrameMetadata();
                decoder.Rewind();
                _frameDurations = DecodeAnimatedImageToGpu(decoder, info, animationMetadata);

                if (_frameDurations.Length > 0)
                {
                    _currentFrameIndex = 0;
                    if (_frameDurations.Length > 1)
                        StartAnimation();
                }
            }
            else
            {
                DecodeStaticImageToGpu(decoder, info);
            }

            // Configure Metal view for the display mode
            if (_displayAsSdr)
            {
                _metalView!.ConfigureForLinearSrgb();
                _metalView.HdrBrightnessScale = 1.0f;
            }
            else if (isHlg)
            {
                _metalView!.ConfigureForHlg();
            }
            else if (isPq)
            {
                _metalView!.ConfigureForPq(info.ToneMapping.IntensityTarget, info.ToneMapping.MinNits);
            }
            else if (info.IsHdr)
            {
                _metalView!.ConfigureForLinear();
            }

            UpdateHdrLabel();
            UpdateHdrToggle();
            _metalView!.Render();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DisplayMode] Error: {ex.Message}");
            _statusLabel!.StringValue = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Updates the HDR/SDR toggle button and context menu item visibility and text.
    /// </summary>
    private void UpdateHdrToggle()
    {
        var isHdrImage = _metadata != null &&
            (_metadata.IsHlg || _metadata.IsPq || _metadata.BasicInfo.IsHdr);

        _hdrSdrMenuItem!.Hidden = !isHdrImage || _comparisonMode;
        _comparisonMenuItem!.Hidden = !isHdrImage;

        if (isHdrImage)
        {
            _hdrSdrMenuItem.Title = _displayAsSdr
                ? "Display as HDR"
                : "Display as SDR";
            _comparisonMenuItem.Title = _comparisonMode
                ? "Exit Comparison"
                : "Compare HDR vs SDR";
        }

        // Sync menu bar items (disabled/greyed when not HDR, vs hidden in context menu)
        if (_hdrSdrMenuBarItem != null)
        {
            _hdrSdrMenuBarItem.Enabled = !_hdrSdrMenuItem!.Hidden;
            _hdrSdrMenuBarItem.Title = isHdrImage
                ? (_displayAsSdr ? "Display as HDR" : "Display as SDR")
                : "Display as SDR";
        }
        if (_comparisonMenuBarItem != null)
        {
            _comparisonMenuBarItem.Enabled = isHdrImage;
            _comparisonMenuBarItem.Title = _comparisonMode
                ? "Exit Comparison"
                : "Compare HDR vs SDR";
        }
    }

    /// <summary>
    /// Shows a popup menu from the HDR label button with display mode options.
    /// </summary>
    private void ShowHdrMenu()
    {
        if (_metadata == null) return;
        var isHdrImage = _metadata.IsHlg || _metadata.IsPq || _metadata.BasicInfo.IsHdr;
        if (!isHdrImage) return;

        var menu = new NSMenu();
        if (!_comparisonMode)
        {
            var toggleTitle = _displayAsSdr ? "Display as HDR" : "Display as SDR";
            menu.AddItem(new NSMenuItem(toggleTitle, (s, e) => ToggleHdrSdr()));
        }
        var comparisonTitle = _comparisonMode ? "Exit Comparison" : "Compare HDR vs SDR";
        menu.AddItem(new NSMenuItem(comparisonTitle, (s, e) => ToggleComparisonMode()));
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Copy Metadata", (s, e) => CopyMetadata()));

        // Show the menu below the HDR label button
        menu.PopUpMenu(null, new CGPoint(0, _hdrLabel!.Frame.Height), _hdrLabel);
    }

    /// <summary>
    /// Toggles comparison mode on/off.
    /// </summary>
    private void ToggleComparisonMode()
    {
        if (_comparisonMode)
            ExitComparisonMode();
        else
            EnterComparisonMode();
        UpdateHdrToggle();
    }

    /// <summary>
    /// Enters HDR vs SDR comparison mode, showing HDR on the left and SDR on the right
    /// with a draggable divider.
    /// </summary>
    private async void EnterComparisonMode()
    {
        if (_comparisonMode || _currentFilePath == null || _metadata == null || _currentInfo == null)
            return;

        var isHdrImage = _metadata.IsHlg || _metadata.IsPq || _metadata.BasicInfo.IsHdr;
        if (!isHdrImage) return;

        Console.WriteLine("[Comparison] Entering comparison mode");

        // If currently showing SDR, reload as HDR first
        if (_displayAsSdr)
        {
            _displayAsSdr = false;
            ReloadWithDisplayMode();
        }

        // Pause animation if playing
        _wasPlayingBeforeComparison = _isPlaying;
        if (_isPlaying)
        {
            StopAnimation();
            UpdatePlayPauseButton();
        }

        try
        {
            // Decode SDR version
            var options = JxlDecodeOptions.Default;
            options.PremultiplyAlpha = true;
            options.PixelFormat = JxlPixelFormat.Rgba32F;
            options.ToneMappingMethod = JxlToneMappingMethod.Bt2446aPerceptual; // BT.2446a tone mapping to SDR

            using var decoder = new JxlDecoder(options);
            await decoder.SetInputFileAsync(_currentFilePath);
            decoder.ReadInfo();

            // Set output to linear sRGB for SDR (same as regular SDR images)
            if (_metadata.IsHlg || _metadata.IsPq)
            {
                using var profile = JxlColorProfile.CreateLinearSrgb();
                decoder.SetOutputColorProfile(profile);
            }

            // Create SDR Metal view with same frame as HDR view
            _sdrMetalView = new HdrMetalView(_metalView!.Frame)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                PassThroughEvents = true
            };

            // Decode SDR image to GPU
            var width = (int)_currentInfo.Size.Width;
            var height = (int)_currentInfo.Size.Height;
            _sdrMetalView.DecodeDirectToGpu(width, height, pixelSpan =>
            {
                decoder.GetPixels(MemoryMarshal.AsBytes(pixelSpan));
            });

            // Configure for linear sRGB display (same as regular SDR images)
            _sdrMetalView.ConfigureForLinearSrgb();
            _sdrMetalView.HdrBrightnessScale = 1.0f;

            // Sync viewport to match HDR view
            _sdrMetalView.SetViewportSilently(_metalView.Zoom, _metalView.Offset);

            // Create divider
            _dividerPosition = 0.5f;
            _comparisonDivider = new ComparisonDividerView(_metalView.Frame)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                DividerPosition = _dividerPosition,
                OnDividerMoved = OnDividerMoved
            };

            // Create side labels
            _hdrSideLabel = CreateComparisonLabel("HDR");
            _sdrSideLabel = CreateComparisonLabel("SDR");

            // Add views: SDR on top of HDR, divider on top of both, labels on top
            var contentView = ContentView!;
            contentView.AddSubview(_sdrMetalView, NSWindowOrderingMode.Above, _metalView);
            contentView.AddSubview(_comparisonDivider, NSWindowOrderingMode.Above, _sdrMetalView);
            contentView.AddSubview(_hdrSideLabel, NSWindowOrderingMode.Above, _comparisonDivider);
            contentView.AddSubview(_sdrSideLabel, NSWindowOrderingMode.Above, _comparisonDivider);

            // Wire viewport sync: when HDR view changes, update SDR view
            _metalView.OnViewportChanged = (zoom, offset) =>
            {
                _sdrMetalView?.SetViewportSilently(zoom, offset);
                UpdateComparisonLayout();
            };

            _comparisonMode = true;
            UpdateComparisonLayout();

            // Defer window activation to the next run loop iteration.
            // This is needed because comparison mode is entered from a popup menu handler;
            // the menu's event tracking is still active, so hit testing and cursor tracking
            // won't work until the current event cycle completes.
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                MakeKeyAndOrderFront(this);
                MakeFirstResponder(_metalView);
                _comparisonDivider?.Window?.InvalidateCursorRectsForView(_comparisonDivider);
            });

            Console.WriteLine("[Comparison] Comparison mode active");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Comparison] Error entering comparison mode: {ex.Message}");
            ExitComparisonMode();
        }
    }

    /// <summary>
    /// Exits comparison mode and cleans up the SDR view and divider.
    /// </summary>
    private void ExitComparisonMode()
    {
        if (!_comparisonMode && _sdrMetalView == null) return;

        Console.WriteLine("[Comparison] Exiting comparison mode");

        _metalView!.OnViewportChanged = null;

        _hdrSideLabel?.RemoveFromSuperview();
        _hdrSideLabel?.Dispose();
        _hdrSideLabel = null;

        _sdrSideLabel?.RemoveFromSuperview();
        _sdrSideLabel?.Dispose();
        _sdrSideLabel = null;

        _comparisonDivider?.RemoveFromSuperview();
        _comparisonDivider?.Dispose();
        _comparisonDivider = null;

        _sdrMetalView?.RemoveFromSuperview();
        _sdrMetalView?.Dispose();
        _sdrMetalView = null;

        _comparisonMode = false;

        // Resume animation if it was playing before
        if (_wasPlayingBeforeComparison && _frameDurations != null && _frameDurations.Length > 1)
        {
            StartAnimation();
            UpdatePlayPauseButton();
        }
        _wasPlayingBeforeComparison = false;

        _metalView.Render();
    }

    /// <summary>
    /// Called when the divider is dragged. Updates the scissor rect and label positions.
    /// </summary>
    private void OnDividerMoved(nfloat position)
    {
        _dividerPosition = position;
        UpdateComparisonLayout();
    }

    /// <summary>
    /// Updates the scissor rect on the SDR view and repositions the labels based on divider position.
    /// </summary>
    private void UpdateComparisonLayout()
    {
        if (_sdrMetalView == null || _comparisonDivider == null || _metalView == null) return;

        var contentsScale = _metalView.Window?.Screen?.BackingScaleFactor
            ?? NSScreen.MainScreen?.BackingScaleFactor
            ?? (nfloat)2.0;

        var viewWidth = _sdrMetalView.Bounds.Width;
        var viewHeight = _sdrMetalView.Bounds.Height;
        if (viewWidth <= 0 || viewHeight <= 0) return;

        var drawableWidth = (nuint)(double)(viewWidth * contentsScale);
        var drawableHeight = (nuint)(double)(viewHeight * contentsScale);
        var dividerPixelX = (nuint)Math.Clamp(
            (double)(_dividerPosition * viewWidth * contentsScale),
            0, (double)(drawableWidth - 1));

        var clipWidth = drawableWidth - dividerPixelX;
        if (clipWidth == 0) clipWidth = 1;

        _sdrMetalView.ScissorRect = new MTLScissorRect
        {
            X = dividerPixelX,
            Y = 0,
            Width = clipWidth,
            Height = drawableHeight
        };

        _sdrMetalView.Render();

        // Update side label positions: HDR to left of divider, SDR to right
        var dividerX = viewWidth * _dividerPosition;
        var labelY = _metalView.Frame.GetMaxY() - 30;

        if (_hdrSideLabel != null)
        {
            var w = _hdrSideLabel.Frame.Width;
            var h = _hdrSideLabel.Frame.Height;
            _hdrSideLabel.Frame = new CGRect(dividerX - w - LabelGap, labelY, w, h);
        }
        if (_sdrSideLabel != null)
        {
            var w = _sdrSideLabel.Frame.Width;
            var h = _sdrSideLabel.Frame.Height;
            _sdrSideLabel.Frame = new CGRect(dividerX + LabelGap, labelY, w, h);
        }
    }

    private const float LabelPaddingH = 5f;
    private const float LabelGap = 6f;

    private static NSTextField CreateComparisonLabel(string text)
    {
        var font = NSFont.BoldSystemFontOfSize(12)!;

        var label = new NSTextField
        {
            StringValue = text,
            Editable = false,
            Selectable = false,
            Bordered = false,
            DrawsBackground = true,
            BackgroundColor = NSColor.Black.ColorWithAlphaComponent(0.5f),
            TextColor = NSColor.White,
            Font = font,
            Alignment = NSTextAlignment.Center,
            TranslatesAutoresizingMaskIntoConstraints = true,
        };

        // SizeToFit gives the natural text height (vertically centered)
        label.SizeToFit();
        var fittedSize = label.Frame.Size;

        // Add horizontal padding, keep the fitted height as-is
        label.Frame = new CGRect(0, 0,
            Math.Ceiling(fittedSize.Width) + LabelPaddingH * 2,
            Math.Ceiling(fittedSize.Height));

        return label;
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

    private void CopyImageToClipboard()
    {
        var isAnimated = _frameDurations is { Length: > 1 };
        CopyImageToClipboard(isAnimated ? ImageFormat.Gif : ImageFormat.Png);
    }

    private void CopyImageToClipboard(ImageFormat format)
    {
        if (_currentFilePath == null || _currentInfo == null) return;

        var filePath = _currentFilePath;
        var info = _currentInfo;
        var isHdr = _metadata is { IsHlg: true } or { IsPq: true };
        var toneMapping = isHdr ? JxlToneMappingMethod.Bt2446aPerceptual : JxlToneMappingMethod.None;

        if (format == ImageFormat.Gif)
        {
            var frameDurations = _frameDurations;
            if (frameDurations == null || frameDurations.Length <= 1) return;

            // GIF encoding uses only CoreGraphics/ImageIO — all safe on background thread
            RunWithSpinner(
                () => EncodeAnimatedGif(filePath, info, frameDurations, toneMapping),
                gifData => CopyToPasteboard(gifData, format, filePath),
                "Copying...");
        }
        else
        {
            var width = (int)info.Size.Width;
            var height = (int)info.Size.Height;
            var frameIndex = _currentFrameIndex;

            RunWithSpinner(() =>
            {
                using var decoder = CreateSrgbDecoder(filePath, toneMapping);
                if (frameIndex > 0)
                    decoder.SeekToFrame(frameIndex);
                var pixels = new byte[width * height * 4];
                decoder.GetPixels(pixels);
                return pixels;
            },
            pixels =>
            {
                var quality = format == ImageFormat.Jpeg ? 0.85f : 0f;
                var data = EncodePixels(pixels, width, height, format, quality);
                CopyToPasteboard(data, format, filePath);
            }, "Copying...");
        }
    }

    private static void CopyToPasteboard(NSData? data, ImageFormat format, string sourceFilePath)
    {
        if (data == null) return;

        var ext = format switch
        {
            ImageFormat.Png => "png",
            ImageFormat.Jpeg => "jpg",
            ImageFormat.Tiff => "tiff",
            ImageFormat.Gif => "gif",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported clipboard format")
        };
        var pasteboardType = format switch
        {
            ImageFormat.Png => NSPasteboardType.Png.GetConstant()!,
            ImageFormat.Jpeg => UTTypes.Jpeg.Identifier,
            ImageFormat.Tiff => NSPasteboardType.Tiff.GetConstant()!,
            ImageFormat.Gif => UTTypes.Gif.Identifier,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported clipboard format")
        };

        // Write to temp file so the file URL can be placed on the pasteboard.
        // Apps like Messages, Slack, and Discord handle file URLs better than
        // raw image data (especially for animated GIFs).
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{fileName}.{ext}");
        data.Save(NSUrl.FromFilename(tempPath), atomically: true);

        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetDataForType(data, pasteboardType);
        pasteboard.SetStringForType(NSUrl.FromFilename(tempPath).AbsoluteString!, NSPasteboardType.FileUrl.GetConstant()!);
        Console.WriteLine($"[Clipboard] Copied {data.Length} bytes as {pasteboardType} + file URL ({tempPath})");
    }

    private NSMenuItem CreateCopyAsMenuItem()
    {
        var copyAsItem = new NSMenuItem("Copy As");
        var copyAsMenu = new NSMenu();
        copyAsMenu.Delegate = new CopyAsMenuDelegate(this);
        copyAsItem.Submenu = copyAsMenu;
        return copyAsItem;
    }

    private sealed class CopyAsMenuDelegate(MainWindow window) : NSMenuDelegate
    {
        public override void NeedsUpdate(NSMenu menu)
        {
            menu.RemoveAllItems();
            menu.AddItem(new NSMenuItem("PNG", (s, e) => window.CopyImageToClipboard(ImageFormat.Png)));
            menu.AddItem(new NSMenuItem("JPEG", (s, e) => window.CopyImageToClipboard(ImageFormat.Jpeg)));
            menu.AddItem(new NSMenuItem("TIFF", (s, e) => window.CopyImageToClipboard(ImageFormat.Tiff)));
            var isAnimated = window._frameDurations != null && window._frameDurations.Length > 1;
            if (isAnimated)
                menu.AddItem(new NSMenuItem("GIF", (s, e) => window.CopyImageToClipboard(ImageFormat.Gif)));
        }
    }

    private void RunWithSpinner(Action backgroundWork, Action? onComplete = null, string? message = null)
    {
        RunWithSpinner(() => { backgroundWork(); return true; }, _ => onComplete?.Invoke(), message);
    }

    private void RunWithSpinner<T>(Func<T> backgroundWork, Action<T> onComplete, string? message = null)
    {
        var backdrop = new NSView
        {
            WantsLayer = true,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        backdrop.Layer!.BackgroundColor = new CGColor(0, 0, 0, 0.6f);
        backdrop.Layer.CornerRadius = 16;

        var spinner = new NSProgressIndicator(new CGRect(0, 0, 64, 64))
        {
            Style = NSProgressIndicatorStyle.Spinning,
            ControlSize = NSControlSize.Large,
            IsDisplayedWhenStopped = false,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        backdrop.AddSubview(spinner);

        NSTextField? label = null;
        if (message != null)
        {
            label = new NSTextField
            {
                StringValue = message,
                Editable = false,
                Selectable = false,
                Bordered = false,
                DrawsBackground = false,
                Font = NSFont.SystemFontOfSize(12, NSFontWeight.Medium)!,
                TextColor = NSColor.White,
                Alignment = NSTextAlignment.Center,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            backdrop.AddSubview(label);
        }

        var contentView = ContentView!;
        contentView.AddSubview(backdrop);
        var constraints = new List<NSLayoutConstraint>
        {
            backdrop.CenterXAnchor.ConstraintEqualTo(contentView.CenterXAnchor),
            backdrop.CenterYAnchor.ConstraintEqualTo(contentView.CenterYAnchor),
            backdrop.WidthAnchor.ConstraintGreaterThanOrEqualTo(96),
            backdrop.HeightAnchor.ConstraintGreaterThanOrEqualTo(96),
            spinner.CenterXAnchor.ConstraintEqualTo(backdrop.CenterXAnchor),
            spinner.TopAnchor.ConstraintEqualTo(backdrop.TopAnchor, 16),
        };
        if (label != null)
        {
            constraints.Add(label.TopAnchor.ConstraintEqualTo(spinner.BottomAnchor, 8));
            constraints.Add(label.CenterXAnchor.ConstraintEqualTo(backdrop.CenterXAnchor));
            constraints.Add(label.LeadingAnchor.ConstraintGreaterThanOrEqualTo(backdrop.LeadingAnchor, 12));
            constraints.Add(label.TrailingAnchor.ConstraintLessThanOrEqualTo(backdrop.TrailingAnchor, -12));
            constraints.Add(label.BottomAnchor.ConstraintEqualTo(backdrop.BottomAnchor, -12));
        }
        else
        {
            constraints.Add(spinner.BottomAnchor.ConstraintEqualTo(backdrop.BottomAnchor, -16));
        }
        NSLayoutConstraint.ActivateConstraints(constraints.ToArray());
        spinner.StartAnimation(null);

        Task.Run(() =>
        {
            try
            {
                var result = backgroundWork();
                NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    try { onComplete(result); }
                    finally
                    {
                        spinner.StopAnimation(null);
                        backdrop.RemoveFromSuperview();
                    }
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    spinner.StopAnimation(null);
                    backdrop.RemoveFromSuperview();
                    ShowAlert("Error", ex.Message);
                });
            }
        });
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
            ExitComparisonMode();
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
                    popup.Cell.ArrowPosition = NSPopUpArrowPosition.None;

                    // First item is the button title (shown when closed)
                    popup.AddItem("File ⌵");
                    popup.AddItem("Open...");
                    popup.AddItem("Export...");

                    // Handle selection — defer to next run loop iteration so the popup's
                    // event tracking completes before the modal dialog starts, otherwise
                    // the dialog UI is partially unresponsive.
                    var window = _window;  // Capture for closure
                    popup.Activated += (s, e) =>
                    {
                        var selectedIndex = popup.IndexOfSelectedItem;
                        popup.SelectItem(0);  // Reset to show "File"
                        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                        {
                            switch (selectedIndex)
                            {
                                case 1: window.OpenFile(); break;
                                case 2: window.ExportImage(); break;
                            }
                        });
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
                    _window._fitButton = new NSButton
                    {
                        Title = "Fit",
                        BezelStyle = NSBezelStyle.Toolbar,
                    };
                    _window._fitButton.SetButtonType(NSButtonType.Toggle);
                    _window._fitButton.Activated += (s, e) => _window.ToggleFitMode();
                    item.Label = "Fit";
                    item.ToolTip = "Fit image to window";
                    item.View = _window._fitButton;
                    break;
            }

            return item;
        }
    }
}
