using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class MainWindow : Window
{
    private bool closingRequested, closingAllowed;
    public MainWindow() { InitializeComponent(); Closing += OnClosing; }

    private void RevealEnvironmentSection(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Control.IsVisibleProperty || args.NewValue is not true || sender is not Control section) return;
        // 等菜单收起及布局完成后再滚动，焦点落在取消上，避免菜单恢复焦点把确认区滚走。
        Dispatcher.UIThread.Post(() =>
        {
            if (!section.IsEffectivelyVisible) return;
            section.UpdateLayout();
            section.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Classes.Contains("removal-cancel"))?.Focus();
            section.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private async void ChooseToolsDirectory(object? sender, RoutedEventArgs args) =>
        await ChooseDirectory("选择组件安装位置", path =>
        {
            if (DataContext is MainWindowViewModel model) model.Environment.ToolsDirectoryInput = path;
        });

    private async void ChooseWorkDirectory(object? sender, RoutedEventArgs args) =>
        await ChooseDirectory("选择工作目录", path =>
        {
            if (DataContext is MainWindowViewModel model) model.Environment.WorkDirectoryInput = path;
        });

    private async Task ChooseDirectory(string title, Action<string> accept)
    {
        if (DataContext is MainWindowViewModel current && !current.Environment.CanChangePaths) return;
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = title, AllowMultiple = false });
        if (selected.Count == 1 && selected[0].TryGetLocalPath() is { } path) accept(path);
    }

    private void OpenComponentGuide(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: EnvironmentComponentViewModel row }) return;
        try
        {
            if (Uri.TryCreate(row.GuideUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
                Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { if (DataContext is MainWindowViewModel model) model.Single.ReportInteractionMessage("无法打开指南：" + ex.Message); }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingAllowed || DataContext is not MainWindowViewModel model) return;
        // 窗口退出必须等相关工具停止，避免窗口消失后进程仍写成品。
        args.Cancel = true;
        if (closingRequested) return;
        closingRequested = true;
        try
        {
            await Task.WhenAll(model.Single.CancelAndWaitAsync(), model.Environment.CancelAndWaitAsync());
            // 界面偏好失败不改变工具退出结论；列宽保存结束后才关闭窗口。
            try { await model.Single.TrackColumns.PersistAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { model.Single.AppendLog("列宽偏好保存失败：" + ex.Message); }
            closingAllowed = true;
            Dispatcher.UIThread.Post(Close);
        }
        catch (Exception ex)
        {
            // 无法确认工具退出时保留窗口，不能用超时或异常绕过安全等待。
            model.Single.AppendLog("退出未完成，窗口保持打开：" + ex.Message);
            model.Single.ReportInteractionMessage("退出未完成，窗口保持打开：" + ex.Message);
            closingRequested = false;
        }
    }
}
