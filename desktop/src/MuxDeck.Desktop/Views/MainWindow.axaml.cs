using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class MainWindow : Window
{
    private bool closingRequested, closingAllowed;
    public MainWindow() { InitializeComponent(); Closing += OnClosing; }

    // 选择器属于视图；路径、探测和任务结果由 ViewModel 管理。
    private async void PickVideo(object? sender, RoutedEventArgs args) => await PickFileAsync("video");
    private async void PickSc(object? sender, RoutedEventArgs args) => await PickFileAsync("sc");
    private async void PickTc(object? sender, RoutedEventArgs args) => await PickFileAsync("tc");
    private async Task PickFileAsync(string role)
    {
        if (DataContext is not MainWindowViewModel model || !model.Single.CanEdit) return;
        try
        {
            var video = role == "video";
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = video ? "选择源视频" : role == "sc" ? "选择简体 UTF-8 ASS" : "选择繁体 UTF-8 ASS", AllowMultiple = false,
                FileTypeFilter = video ? [new FilePickerFileType("视频文件") { Patterns = ["*.mkv", "*.mp4", "*.avi", "*.mov", "*.ts", "*.m2ts"] }, FilePickerFileTypes.All] : [new FilePickerFileType("ASS 字幕") { Patterns = ["*.ass"] }]
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path || !model.Single.CanEdit) return;
            if (video) model.Single.VideoPath = path;
            else if (role == "sc") model.Single.ScSubtitle = path;
            else model.Single.TcSubtitle = path;
        }
        catch (Exception ex) { model.Single.AppendLog("文件选择失败：" + ex.Message); }
    }
    private async void PickFonts(object? sender, RoutedEventArgs args) => await PickFolderAsync(true);
    private async void PickOutput(object? sender, RoutedEventArgs args) => await PickFolderAsync(false);
    private async Task PickFolderAsync(bool fonts)
    {
        if (DataContext is not MainWindowViewModel model || !model.Single.CanEdit) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = fonts ? "选择字体目录" : "选择独立输出目录", AllowMultiple = false });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path || !model.Single.CanEdit) return;
            if (fonts) model.Single.FontsDirectory = path; else model.Single.OutputDirectory = path;
        }
        catch (Exception ex) { model.Single.AppendLog("目录选择失败：" + ex.Message); }
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
            await model.Single.CancelAndWaitAsync();
            closingAllowed = true;
            Dispatcher.UIThread.Post(Close);
        }
        catch (Exception ex)
        {
            // 无法确认工具退出时保留窗口，不能用超时或异常绕过安全等待。
            model.Single.AppendLog("退出未完成，窗口保持打开：" + ex.Message);
            closingRequested = false;
        }
    }
}
