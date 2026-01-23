using AppKit;
using Foundation;

namespace JpegXL.MacOS;

[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    private MainWindow? _mainWindow;

    public override void DidFinishLaunching(NSNotification notification)
    {
        _mainWindow = new MainWindow();
        _mainWindow.MakeKeyAndOrderFront(this);

        // Load file from command line if provided
        if (!string.IsNullOrEmpty(Program.InitialFilePath))
        {
            _mainWindow.LoadImage(Program.InitialFilePath);
        }
    }

    public override void WillTerminate(NSNotification notification)
    {
        _mainWindow?.Dispose();
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override bool OpenFile(NSApplication sender, string filename)
    {
        _mainWindow?.LoadImage(filename);
        return true;
    }
}
