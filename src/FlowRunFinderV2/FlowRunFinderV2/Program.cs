using Avalonia;
using System.Runtime.InteropServices;

namespace FlowRunFinderV2;

internal static class Program
{
    private const string AppUserModelId = "FlowRunFinderV2.Desktop";

    [STAThread]
    public static void Main(string[] args)
    {
        SetWindowsAppUserModelId();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    private static void SetWindowsAppUserModelId()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
