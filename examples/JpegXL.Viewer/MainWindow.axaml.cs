using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using JpegXL.Net;

namespace JpegXL.Viewer;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private JxlImage? _currentImage;

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
            StatusText!.Text = "Loading...";
            
            // Load on background thread
            var bytes = await File.ReadAllBytesAsync(path);
            
            // Decode on background thread with premultiplied alpha for Avalonia
            var options = new JxlDecodeOptions { PremultiplyAlpha = true };
            _currentImage?.Dispose();
            _currentImage = await Task.Run(() => JxlImage.Decode(bytes, JxlPixelFormat.Bgra8, options));
            
            // Convert to Avalonia bitmap
            var bitmap = CreateBitmapFromJxlImage(_currentImage);
            
            ImageDisplay!.Source = bitmap;
            DropHint!.IsVisible = false;
            
            // Update UI
            _zoom = 1.0;
            UpdateZoom();
            
            var info = _currentImage.BasicInfo;
            ImageInfoText!.Text = $"{info.Width}×{info.Height} | {info.BitsPerSample}bpp | {(info.HasAlpha ? "RGBA" : "RGB")}";
            StatusText.Text = $"Loaded: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText!.Text = $"Error: {ex.Message}";
            ImageInfoText!.Text = "";
        }
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
        _currentImage?.Dispose();
        base.OnClosed(e);
    }
}
