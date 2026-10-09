using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Native picker belongs to the view. File paths and task state belong to the view model.
    private async void PickVideo(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not MainWindowViewModel model) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择源视频", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("视频文件")
                    { Patterns = ["*.mkv", "*.mp4", "*.avi", "*.mov", "*.ts", "*.m2ts"] }, FilePickerFileTypes.All]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                model.VideoPath = path;
                model.AppendLog("已选择视频：" + Path.GetFileName(path));
            }
        }
        catch (Exception ex) { model.AppendLog("文件选择失败：" + ex.Message); }
    }
}
