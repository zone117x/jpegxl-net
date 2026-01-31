using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JpegXL.Net;

namespace JpegXL.Viewer;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private string _colorProfileDescription = "";

    // Pan/zoom state
    private Point _offset = new(0, 0);
    private bool _isDragging;
    private Point _dragStartLocation;
    private Point _dragStartOffset;

    // Animation support
    private List<AnimationFrame>? _frames;
    private int _currentFrameIndex;
    private DispatcherTimer? _animationTimer;
    private bool _isPlaying;
    private Stopwatch? _animationStopwatch;
    private double _frameStartTime;

    private record AnimationFrame(WriteableBitmap Bitmap, float DurationMs);

    public MainWindow()
    {
        InitializeComponent();
        
        // Set up drag and drop
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        
        // Load file from command line if provided (e.g., "Open With" from Finder)
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Program.InitialFilePath))
        {
            await LoadImageAsync(Program.InitialFilePath);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) 
            ? DragDropEffects.Copy 
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
            return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var file in files)
        {
            if (file is IStorageFile storageFile)
            {
                var path = storageFile.Path.LocalPath;
                if (path.EndsWith(".jxl", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadImageAsync(path);
                    break;
                }
            }
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open JPEG XL Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JPEG XL Images") { Patterns = new[] { "*.jxl" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0)
        {
            await LoadImageAsync(files[0].Path.LocalPath);
        }
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            StopAnimation();
            StatusText!.Text = "Loading...";
            HdrInfoText!.IsVisible = false;
            ColorProfileText!.Text = "";
            ClearFrames();

            // Single decoder for entire operation
            // Set DesiredIntensityTarget for HDR→SDR tone mapping (only affects HDR images)
            var options = new JxlDecodeOptions
            {
                PremultiplyAlpha = true,
                DesiredIntensityTarget = 255f
            };
            using var decoder = new JxlDecoder(options);

            // Async file I/O keeps UI responsive
            await decoder.SetInputFileAsync(path);

            var info = decoder.ReadInfo();
            var isHdr = info.IsHdr;

            // Extract color profile (same decoder, quick operation)
            _colorProfileDescription = GetColorProfileDescription(decoder);
            ColorProfileText.Text = _colorProfileDescription;

            decoder.SetPixelFormat(JxlPixelFormat.Bgra8);

            if (info.IsAnimated)
            {
                // Pass 1: Get metadata (uses SkipFrame internally, no pixel buffers)
                var metadata = decoder.ParseFrameMetadata(maxFrames: 1000);
                var frameDurations = metadata.Frames.Select(h => h.DurationMs > 0 ? h.DurationMs : 0.1f).ToArray();

                // Rewind for pixel decoding (pixel format preserved)
                decoder.Rewind();

                // Pass 2: Decode frames on background thread (same decoder, reusable buffer)
                _frames = await Task.Run(() => DecodeAnimatedFrames(decoder, info, frameDurations));

                if (_frames.Count > 0)
                {
                    _currentFrameIndex = 0;
                    ImageDisplay!.Source = _frames[0].Bitmap;
                    DropHint!.IsVisible = false;

                    // Start playback if we have multiple frames
                    if (_frames.Count > 1)
                    {
                        StartAnimation();
                    }

                    var formatStr = isHdr ? "HDR" : "Animated";
                    ImageInfoText!.Text = $"{info.Size.Width}×{info.Size.Height} | {info.BitDepth.BitsPerSample}bpp | {_frames.Count} frames | {formatStr}";
                    HdrInfoText!.IsVisible = isHdr;
                    if (isHdr)
                    {
                        HdrInfoText.Text = $"HDR: {info.ToneMapping.IntensityTarget:F0} nits";
                    }
                    StatusText.Text = $"Loaded: {Path.GetFileName(path)}";
                    UpdatePlayPauseButton();
                }
            }
            else
            {
                // Static image - decode on background thread (same decoder)
                var bitmap = await Task.Run(() => DecodeStaticToBitmap(decoder, info));
                ImageDisplay!.Source = bitmap;
                DropHint!.IsVisible = false;

                var formatStr = isHdr ? "HDR" : (info.HasAlpha ? "RGBA" : "RGB");
                ImageInfoText!.Text = $"{info.Size.Width}×{info.Size.Height} | {info.BitDepth.BitsPerSample}bpp | {formatStr}";
                HdrInfoText!.IsVisible = isHdr;
                if (isHdr)
                {
                    HdrInfoText.Text = $"HDR: {info.ToneMapping.IntensityTarget:F0} nits";
                }
                StatusText.Text = $"Loaded: {Path.GetFileName(path)}";
                UpdatePlayPauseButton();
            }

            // Reset zoom and offset
            _zoom = 1.0;
            _offset = new Point(0, 0);
            UpdateZoom();
        }
        catch (Exception ex)
        {
            StatusText!.Text = $"Error: {ex.Message}";
            ImageInfoText!.Text = "";
            ColorProfileText!.Text = "";
        }
    }

    /// <summary>
    /// Decodes a static image to a bitmap using the provided decoder.
    /// </summary>
    private static WriteableBitmap DecodeStaticToBitmap(JxlDecoder decoder, JxlBasicInfo info)
    {
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;
        var pixels = decoder.GetPixels();
        return CreateBitmapFromPixels(pixels, width, height);
    }

    /// <summary>
    /// Decodes animated frames using the provided decoder and pre-collected frame durations.
    /// Expects decoder to be rewound with pixel format already set.
    /// </summary>
    private List<AnimationFrame> DecodeAnimatedFrames(JxlDecoder decoder, JxlBasicInfo info, float[] frameDurations)
    {
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;
        var bufferSize = width * height * 4; // BGRA

        var frames = new List<AnimationFrame>(frameDurations.Length);
        var pixels = new byte[bufferSize]; // Single reusable buffer
        var frameIndex = 0;

        // Need to advance past basic info after rewind
        decoder.ReadInfo();

        while (decoder.HasMoreFrames() && frameIndex < frameDurations.Length)
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
                evt = decoder.ReadPixels(pixels);

                if (evt == JxlDecoderEvent.FrameComplete)
                {
                    // Create bitmap directly from reusable buffer (Marshal.Copy copies the data)
                    var bitmap = CreateBitmapFromPixels(pixels, width, height);
                    frames.Add(new AnimationFrame(bitmap, frameDurations[frameIndex]));
                    frameIndex++;
                }
            }
        }

        return frames;
    }

    private static WriteableBitmap CreateBitmapFromPixels(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var frameBuffer = bitmap.Lock();
        Marshal.Copy(pixels, 0, frameBuffer.Address, pixels.Length);

        return bitmap;
    }

    private static string GetColorProfileDescription(JxlDecoder decoder)
    {
        try
        {
            using var profile = decoder.GetEmbeddedColorProfile();

            if (profile.IsIcc && profile.IccData != null && profile.IccData.Length > 0)
            {
                // Try to parse ICC profile description
                var iccName = IccProfileParser.TryGetDescription(profile.IccData);
                return iccName != null ? $"ICC: {iccName}" : "ICC";
            }
            else
            {
                // Use the simple encoding description
                var desc = profile.GetDescription();
                return string.IsNullOrWhiteSpace(desc) ? "" : desc;
            }
        }
        catch
        {
            return "";
        }
    }

    private void StartAnimation()
    {
        if (_frames == null || _frames.Count <= 1) return;

        StopAnimation();

        // Use fixed 60fps timer with elapsed-time tracking for smooth playback
        _animationStopwatch = Stopwatch.StartNew();
        _frameStartTime = 0;

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
        _isPlaying = true;
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        _animationStopwatch?.Stop();
        _animationStopwatch = null;
        _isPlaying = false;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_frames == null || _frames.Count == 0 || _animationStopwatch == null) return;

        var elapsed = _animationStopwatch.Elapsed.TotalMilliseconds;
        var frameDuration = _frames[_currentFrameIndex].DurationMs;

        // Check if it's time to advance to the next frame
        if (elapsed - _frameStartTime >= frameDuration)
        {
            _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
            _frameStartTime = elapsed;
            ImageDisplay!.Source = _frames[_currentFrameIndex].Bitmap;
            FrameInfoText!.Text = $"Frame {_currentFrameIndex + 1}/{_frames.Count}";
        }
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
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

    private void UpdatePlayPauseButton()
    {
        var hasAnimation = _frames != null && _frames.Count > 1;
        PlayPauseButton!.IsVisible = hasAnimation;
        FrameInfoText!.IsVisible = hasAnimation;
        
        if (hasAnimation)
        {
            PlayPauseButton!.Content = _isPlaying ? "⏸ Pause" : "▶ Play";
            FrameInfoText!.Text = $"Frame {_currentFrameIndex + 1}/{_frames!.Count}";
        }
    }

    private void ClearFrames()
    {
        _frames = null;
        _currentFrameIndex = 0;
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(_zoom * 1.25, 100.0);
        ClampOffset();
        UpdateZoom();
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(_zoom / 1.25, 0.1);
        ClampOffset();
        UpdateZoom();
    }

    private void OnActualSizeClick(object? sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        _offset = new Point(0, 0);
        UpdateZoom();
    }

    private void OnFitClick(object? sender, RoutedEventArgs e)
    {
        if (ImageDisplay?.Source == null || ImageBorder == null) return;

        var source = ImageDisplay.Source;
        if (source is not WriteableBitmap bitmap) return;

        var imageWidth = bitmap.PixelSize.Width;
        var imageHeight = bitmap.PixelSize.Height;
        var viewWidth = ImageBorder.Bounds.Width;
        var viewHeight = ImageBorder.Bounds.Height;

        if (viewWidth <= 0 || viewHeight <= 0) return;

        // Calculate zoom to fit entire image in view
        var zoomX = viewWidth / imageWidth;
        var zoomY = viewHeight / imageHeight;
        _zoom = Math.Min(zoomX, zoomY);
        _offset = new Point(0, 0);

        UpdateZoom();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ImageDisplay?.Source == null || ImageBorder == null) return;

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < 0.01) return;

        // Calculate zoom factor (positive delta = zoom in)
        var zoomFactor = 1.0 + delta * 0.1;

        // Get mouse position relative to the ImageBorder
        var mousePos = e.GetPosition(ImageBorder);

        // Calculate center of the view
        var viewCenterX = ImageBorder.Bounds.Width / 2;
        var viewCenterY = ImageBorder.Bounds.Height / 2;

        // Store old zoom
        var oldZoom = _zoom;

        // Apply zoom with clamping
        _zoom = Math.Clamp(_zoom * zoomFactor, 0.1, 100.0);

        // Adjust offset to zoom toward cursor
        var zoomRatio = _zoom / oldZoom;

        // Calculate how much the point under cursor should move
        var mouseOffsetX = mousePos.X - viewCenterX;
        var mouseOffsetY = mousePos.Y - viewCenterY;

        _offset = new Point(
            _offset.X * zoomRatio + mouseOffsetX * (1 - zoomRatio),
            _offset.Y * zoomRatio + mouseOffsetY * (1 - zoomRatio)
        );

        ClampOffset();
        UpdateZoom();
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ImageDisplay?.Source == null || ImageBorder == null) return;

        _isDragging = true;
        _dragStartLocation = e.GetPosition(ImageBorder);
        _dragStartOffset = _offset;
        e.Pointer.Capture(ImageBorder);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || ImageBorder == null) return;

        var currentPos = e.GetPosition(ImageBorder);
        var deltaX = currentPos.X - _dragStartLocation.X;
        var deltaY = currentPos.Y - _dragStartLocation.Y;

        _offset = new Point(
            _dragStartOffset.X + deltaX,
            _dragStartOffset.Y + deltaY
        );

        ClampOffset();
        UpdateZoom();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void ClampOffset()
    {
        if (ImageDisplay?.Source == null || ImageBorder == null) return;

        var source = ImageDisplay.Source;
        if (source is not WriteableBitmap bitmap) return;

        var imageWidth = bitmap.PixelSize.Width * _zoom;
        var imageHeight = bitmap.PixelSize.Height * _zoom;
        var viewWidth = ImageBorder.Bounds.Width;
        var viewHeight = ImageBorder.Bounds.Height;

        if (viewWidth <= 0 || viewHeight <= 0) return;

        // If image is smaller than view, don't allow offset (keep centered)
        // If image is larger, allow offset but keep at least 25% of view showing image
        double maxOffsetX = Math.Max(0, (imageWidth - viewWidth) / 2 + viewWidth * 0.25);
        double maxOffsetY = Math.Max(0, (imageHeight - viewHeight) / 2 + viewHeight * 0.25);

        _offset = new Point(
            Math.Clamp(_offset.X, -maxOffsetX, maxOffsetX),
            Math.Clamp(_offset.Y, -maxOffsetY, maxOffsetY)
        );
    }

    private void UpdateZoom()
    {
        if (ImageDisplay == null) return;

        // Create a transform group with scale and translate
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(_zoom, _zoom));
        transformGroup.Children.Add(new TranslateTransform(_offset.X, _offset.Y));

        ImageDisplay.RenderTransform = transformGroup;
        ImageDisplay.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        ZoomText!.Text = $"Zoom: {_zoom:P0}";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAnimation();
        ClearFrames();
        base.OnClosed(e);
    }
}
