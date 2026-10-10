using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class MainWindow : Window
{
    private bool closingRequested, closingAllowed;
    public MainWindow() { InitializeComponent(); Closing += OnClosing; }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingAllowed || DataContext is not MainWindowViewModel model) return;
        // 窗口退出必须等相关工具停止，避免窗口消失后进程仍写成品。
        args.Cancel = true;
        if (closingRequested) return;
        closingRequested = true;
        try
        {
            await model.Single.CancelAndWaitAsync();
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
