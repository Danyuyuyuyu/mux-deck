using Avalonia;
using MuxDeck.Core;
using MuxDeck.Desktop.Preferences;

namespace MuxDeck.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--root"))
            throw new ArgumentException("Usage: MuxDeck.Desktop [--root <workspace directory>]");
        var explicitRoot = args.Length == 2 ? args[1] : null;
        App.Workspace = Workspace.Discover(explicitRoot);
        var portable = !string.IsNullOrWhiteSpace(explicitRoot);
        var fallback = portable ? StoragePaths.ForWorkspace(App.Workspace.Root) : StoragePaths.Default;
        var store = new StorageSettingsStore(StorageSettingsStore.DefaultPath);
        App.Storage = portable ? fallback : store.Load(fallback);
        App.Storage.EnsureDirectories();
        App.Workspace = App.Workspace with { ToolsDirectoryOverride = App.Storage.ToolsDirectory };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
