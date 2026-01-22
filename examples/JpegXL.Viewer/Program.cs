using Avalonia;

namespace JpegXL.Viewer;

class Program
{
    public static string? InitialFilePath { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Capture file path from command line (used by "Open With")
        if (args.Length > 0 && File.Exists(args[0]))
        {
            InitialFilePath = args[0];
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
