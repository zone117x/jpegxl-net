using AppKit;
using Foundation;

namespace JpegXL.MacOS;

[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    private MainWindow? _mainWindow;
    private NSTimer? _exitTimer;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // Bring app to foreground (required when launched from terminal)
        NSApplication.SharedApplication.Activate();

        _mainWindow = new MainWindow();
        _mainWindow.MakeKeyAndOrderFront(this);

        // Load file from command line if provided
        if (!string.IsNullOrEmpty(Program.Args.InputFile))
        {
            _mainWindow.LoadImage(Program.Args.InputFile);
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
        _mainWindow?.LoadImage(filename);
        return true;
    }
}
