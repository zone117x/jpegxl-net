using AppKit;
using Foundation;

namespace JpegXL.MacOS;

[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    private MainWindow? _mainWindow;
    private NSTimer? _exitTimer;
    private string? _pendingOpenFile;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // Bring app to foreground (required when launched from terminal)
        NSApplication.SharedApplication.Activate();

        _mainWindow = new MainWindow();
        _mainWindow.MakeKeyAndOrderFront(this);
        _mainWindow.MakeFirstResponder(_mainWindow.ContentView);

        // Load file: prefer file received via OpenFile (e.g. Finder "Open With"),
        // fall back to command line argument
        var fileToLoad = _pendingOpenFile ?? Program.Args.InputFile;
        _pendingOpenFile = null;
        if (!string.IsNullOrEmpty(fileToLoad))
        {
            _mainWindow.LoadImage(fileToLoad);
        }

        // Set up exit timer if requested
        if (Program.Args.ExitAfterSeconds.HasValue)
        {
            var seconds = Program.Args.ExitAfterSeconds.Value;
            Console.WriteLine($"[AppDelegate] Will exit after {seconds} seconds");
            _exitTimer = NSTimer.CreateScheduledTimer(seconds, false, _ =>
            {
                Console.WriteLine("[AppDelegate] Exit timer fired, terminating");
                NSApplication.SharedApplication.Terminate(null);
            });
        }
    }

    public override void WillTerminate(NSNotification notification)
    {
        _exitTimer?.Invalidate();
        _exitTimer?.Dispose();
        _mainWindow?.Dispose();
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override bool OpenFile(NSApplication sender, string filename)
    {
        if (_mainWindow != null)
        {
            _mainWindow.LoadImage(filename);
        }
        else
        {
            // OpenFile is called before DidFinishLaunching when launched via
            // Finder "Open With" — buffer the path for DidFinishLaunching to pick up
            _pendingOpenFile = filename;
        }
        return true;
    }
}
