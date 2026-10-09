using Avalonia;
using MuxDeck.Core;

namespace MuxDeck.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--root"))
            throw new ArgumentException("Usage: MuxDeck.Desktop [--root <workspace directory>]");
        App.Workspace = Workspace.Discover(args.Length == 2 ? args[1] : null);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
