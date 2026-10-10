using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

// 只包住原生选择、确认和平台动作，测试仍通过真实控件事件与接纳流程。
public interface ISingleMuxInteractions
{
    Task<IReadOnlyList<IStorageItem>> PickAsync(TopLevel owner, InputOperation operation);
    Task<bool> ConfirmVideoAsync(Window owner, string currentPath, string candidatePath);
    Task<IReadOnlyList<SubtitleAssignment>?> AssignSubtitlesAsync(Window owner, SingleMuxViewModel model,
        IReadOnlyList<InputCandidate> candidates, SubtitleSlot? fixedSlot);
    Task CopyAsync(TopLevel owner, string text);
    void OpenLocation(string path);
}

public sealed class SingleMuxInteractions : ISingleMuxInteractions
{
    public async Task<IReadOnlyList<IStorageItem>> PickAsync(TopLevel owner, InputOperation operation)
    {
        if (operation is InputOperation.FontsDirectory or InputOperation.OutputDirectory)
            return await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = operation == InputOperation.FontsDirectory ? "选择字体目录" : "选择独立输出目录", AllowMultiple = false });
        var video = operation == InputOperation.Video;
        return await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = video ? "选择 MKV / MP4 视频" : "选择 1–2 个 UTF-8 ASS 字幕", AllowMultiple = !video,
            FileTypeFilter = [new FilePickerFileType(video ? "MKV / MP4 视频" : "ASS 字幕") { Patterns = video ? ["*.mkv", "*.mp4"] : ["*.ass"] }]
        });
    }
    public async Task<bool> ConfirmVideoAsync(Window owner, string currentPath, string candidatePath)
    {
        var dialog = CreateDialog("确认更换源视频", 600);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "将替换源视频；字幕、字体与输出设置保持。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(PathDisplay("当前视频", currentPath));
        body.Children.Add(PathDisplay("新视频", candidatePath));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var accept = new Button { Content = "确认更换", IsDefault = true }; accept.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close(false); accept.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(cancel); actions.Children.Add(accept); body.Children.Add(actions); dialog.Content = body;
        return await dialog.ShowDialog<bool>(owner);
    }
    public async Task<IReadOnlyList<SubtitleAssignment>?> AssignSubtitlesAsync(Window owner, SingleMuxViewModel model,
        IReadOnlyList<InputCandidate> candidates, SubtitleSlot? fixedSlot)
    {
        var dialog = new SubtitleAssignmentWindow(model, candidates, fixedSlot);
        return await dialog.ShowDialog<IReadOnlyList<SubtitleAssignment>?>(owner);
    }
    public async Task CopyAsync(TopLevel owner, string text)
    {
        var clipboard = owner.Clipboard ?? throw new InvalidOperationException("当前窗口没有可用剪贴板。");
        await clipboard.SetTextAsync(text);
    }
    public void OpenLocation(string path)
    {
        // 路径只作为参数传递；不构造或执行 shell 命令。
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("目标不存在或已经移除。", path);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        if (File.Exists(path)) start.ArgumentList.Add("/select,");
        start.ArgumentList.Add(Path.GetFullPath(path));
        using var process = Process.Start(start) ?? throw new IOException("无法打开资源管理器。");
    }
    internal static Window CreateDialog(string title, double width)
    {
        var dialog = new Window
        {
            Title = title, Width = width, MaxHeight = 650, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        // 弹窗使用应用级字体、背景和控件规范，不在代码里复制主题数值。
        dialog.Classes.Add("dialog");
        return dialog;
    }
    internal static Control PathDisplay(string label, string path)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(new TextBox { Text = path, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        return panel;
    }
}

public sealed class SubtitleAssignmentWindow : Window
{
    private readonly SingleMuxViewModel model;
    private readonly IReadOnlyList<InputCandidate> candidates;
    private readonly List<ComboBox> slots = [];
    private readonly TextBlock error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public SubtitleAssignmentWindow(SingleMuxViewModel model, IReadOnlyList<InputCandidate> candidates, SubtitleSlot? fixedSlot = null)
    {
        this.model = model; this.candidates = candidates;
        Title = "分配 ASS 字幕"; Width = 620; MaxHeight = 680; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("dialog"); error.Classes.Add("error");
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "明确选择 SC 简体 / TC 繁体；不从文件名推断语言。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var preferred = string.IsNullOrWhiteSpace(model.ScSubtitle) ? 0 : string.IsNullOrWhiteSpace(model.TcSubtitle) ? 1 : 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            body.Children.Add(SingleMuxInteractions.PathDisplay("候选 " + (i + 1), candidates[i].Path));
            var slot = new ComboBox { ItemsSource = new[] { "SC · 简体 · zh-Hans", "TC · 繁体 · zh-Hant" }, SelectedIndex = fixedSlot is { } fixedValue ? (int)fixedValue : (preferred + i) % 2, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = fixedSlot is null };
            var replacement = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            replacement.Classes.Add("muted");
            var candidate = candidates[i];
            void RefreshReplacement()
            {
                var path = slot.SelectedIndex == 0 ? model.ScSubtitle : model.TcSubtitle;
                replacement.Text = string.IsNullOrWhiteSpace(path) ? "该槽位为空，将添加字幕。" : "该槽位已有字幕：" + path + "\n确认后接纳候选；替换保留名称与旗标。";
            }
            slot.SelectionChanged += (_, _) => RefreshReplacement(); RefreshReplacement();
            slots.Add(slot); body.Children.Add(slot); body.Children.Add(replacement);
        }
        body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Name = "CancelAssignment", Content = "取消", IsCancel = true };
        var accept = new Button { Name = "ConfirmAssignment", Content = "确认分配 / 替换", IsDefault = true }; accept.Classes.Add("primary");
        cancel.Click += (_, _) => Close(null);
        accept.Click += (_, _) =>
        {
            var choice = GetAssignments();
            var prepared = model.PrepareSubtitleInputs(choice);
            if (!prepared.Succeeded) { error.Text = prepared.Message; return; }
            Close(choice);
        };
        actions.Children.Add(cancel); actions.Children.Add(accept); body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(null); } };
    }
    public IReadOnlyList<ComboBox> SlotSelectors => slots;
    public IReadOnlyList<SubtitleAssignment> GetAssignments() => candidates.Select((candidate, i) => new SubtitleAssignment((SubtitleSlot)slots[i].SelectedIndex, candidate)).ToArray();
}
