using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MuxDeck.Core;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using MuxDeck.Desktop.Preferences;

namespace MuxDeck.Desktop;

public partial class App : Avalonia.Application
{
    public static Workspace Workspace { get; set; } = Workspace.Discover();
    public static StoragePaths Storage { get; set; } = StoragePaths.Default;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(Workspace, preferences: UserPreferencesStore.CreateDefault(Storage), storage: Storage)
            };
        base.OnFrameworkInitializationCompleted();
    }
}
