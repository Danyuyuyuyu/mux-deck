using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MuxDeck.Desktop.Views;

namespace MuxDeck.Desktop;

public partial class App : Avalonia.Application
{
    private readonly Func<MainWindow>? createMainWindow;
    // Headless 初始化不建立窗口；生产启动由装配提供窗口工厂。
    public App() { }
    public App(Func<MainWindow> createMainWindow) => this.createMainWindow = createMainWindow;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = (createMainWindow ?? throw new InvalidOperationException("窗口工厂尚未装配。"))();
        base.OnFrameworkInitializationCompleted();
    }
}
