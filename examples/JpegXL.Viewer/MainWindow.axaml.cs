using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JpegXL.Net;

namespace JpegXL.Viewer;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private JxlImage? _currentImage;
    
    // Animation support
    private List<AnimationFrame>? _frames;
    private int _currentFrameIndex;
    private DispatcherTimer? _animationTimer;
    private bool _isPlaying;

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

            // Check if animated - use SetInputFile to read directly into native memory
            using var decoder = new JxlDecoder(new JxlDecodeOptions { PremultiplyAlpha = true });
            decoder.SetInputFile(path);
            var info = decoder.ReadInfo();

            _currentImage?.Dispose();
            ClearFrames();

            if (info.IsAnimated)
            {
                // Check if HDR
                var isHdr = info.IsHdr;

                // Decode all frames
                _frames = await Task.Run(() => DecodeAnimatedImage(path, info, isHdr));

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
                // Static image - decode on background thread
                var isHdr = info.IsHdr;
                var staticOptions = new JxlDecodeOptions { PremultiplyAlpha = true };
                if (isHdr)
                {
                    // Tone map HDR to SDR by setting target intensity to 255 nits
                    staticOptions.DesiredIntensityTarget = 255f;
                }

                var pixels = await Task.Run(() => DecodeStaticImage(path, staticOptions));
                _currentImage = null; // Not using JxlImage for file-based decode

                var bitmap = CreateBitmapFromPixels(pixels, (int)info.Size.Width, (int)info.Size.Height);
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
            
            // Update UI
            _zoom = 1.0;
            UpdateZoom();
        }
        catch (Exception ex)
        {
            StatusText!.Text = $"Error: {ex.Message}";
            ImageInfoText!.Text = "";
        }
    }

    private static byte[] DecodeStaticImage(string path, JxlDecodeOptions options)
    {
        using var decoder = new JxlDecoder(options);
        decoder.SetInputFile(path);
        decoder.SetPixelFormat(JxlPixelFormat.Bgra8);
        decoder.ReadInfo();

        return decoder.GetPixels();
    }

    private List<AnimationFrame> DecodeAnimatedImage(string path, JxlBasicInfo info, bool isHdr)
    {
        var frames = new List<AnimationFrame>();
        var width = (int)info.Size.Width;
        var height = (int)info.Size.Height;
        var bufferSize = width * height * 4; // BGRA
        var pixels = new byte[bufferSize];

        var options = new JxlDecodeOptions { PremultiplyAlpha = true };
        if (isHdr)
        {
            // Tone map HDR to SDR by setting target intensity to 255 nits
            options.DesiredIntensityTarget = 255f;
        }

        using var decoder = new JxlDecoder(options);
        decoder.SetInputFile(path);
        decoder.SetPixelFormat(JxlPixelFormat.Bgra8);
        decoder.ReadInfo(); // Advance past basic info
        
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
                    // Create bitmap on UI thread wouldn't work here, so we store raw data
                    var framePixels = new byte[bufferSize];
                    Array.Copy(pixels, framePixels, bufferSize);
                    
                    // Create bitmap (needs to be done carefully for thread safety)
                    var bitmap = CreateBitmapFromPixels(framePixels, width, height);
                    frames.Add(new AnimationFrame(bitmap, duration > 0 ? duration : 0.1f));
                }
            }
            
            if (frames.Count > 1000) break; // Safety limit
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

    private void StartAnimation()
    {
        if (_frames == null || _frames.Count <= 1) return;
        
        StopAnimation();
        
        var interval = Math.Max(0.016, _frames[_currentFrameIndex].DurationMs / 1000.0);
        
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
        _isPlaying = true;
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        _isPlaying = false;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_frames == null || _frames.Count == 0 || _animationTimer == null) return;
        
        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
        ImageDisplay!.Source = _frames[_currentFrameIndex].Bitmap;
        FrameInfoText!.Text = $"Frame {_currentFrameIndex + 1}/{_frames.Count}";
        
        // Update timer interval for next frame - must stop/start for interval change to take effect
        var nextDuration = _frames[_currentFrameIndex].DurationMs / 1000.0;
        _animationTimer.Stop();
        _animationTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.016, nextDuration));
        _animationTimer.Start();
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

    private static WriteableBitmap CreateBitmapFromJxlImage(JxlImage image)
    {
        var width = image.Width;
        var height = image.Height;
        
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var frameBuffer = bitmap.Lock();
        var pixels = image.GetPixelArray();
        
        // Pixels are already premultiplied by the decoder
        Marshal.Copy(pixels, 0, frameBuffer.Address, pixels.Length);

        return bitmap;
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(_zoom * 1.25, 10.0);
        UpdateZoom();
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(_zoom / 1.25, 0.1);
        UpdateZoom();
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        if (ImageDisplay == null) return;
        
        ImageDisplay.RenderTransform = new Avalonia.Media.ScaleTransform(_zoom, _zoom);
        StatusText!.Text = $"Zoom: {_zoom:P0}";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAnimation();
        ClearFrames();
        _currentImage?.Dispose();
        base.OnClosed(e);
    }
}
