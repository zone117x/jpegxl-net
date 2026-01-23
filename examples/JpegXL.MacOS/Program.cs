using AppKit;

namespace JpegXL.MacOS;

static class Program
{
    public static string? InitialFilePath { get; private set; }

    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            InitialFilePath = args[0];
        }

        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new AppDelegate();
        NSApplication.Main(args);
    }
}
