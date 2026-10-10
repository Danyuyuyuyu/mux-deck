using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

internal static class UiInputTestSupport
{
    public static void Pump(Task task, int seconds = 20) { Until(() => task.IsCompleted, seconds); task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
    public static void Until(Func<bool> condition, int seconds = 20)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(seconds)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
        if (!condition()) throw new TimeoutException("UI 操作未在时限内完成。");
    }
    public static void Click(Window window, Control control)
    {
        control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        if (!control.IsEffectivelyEnabled || !control.IsEffectivelyVisible) throw new InvalidOperationException("无法点击禁用或不可见控件：" + control.Name);
        control.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Dispatcher.UIThread.RunJobs();
    }
    public static void Click(Window window, SingleMuxView view, string name) => Click(window, view.FindControl<Control>(name)!);
    public static DragEventArgs Drag(Border zone, Avalonia.Interactivity.RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data)
    {
        var args = new DragEventArgs(routedEvent, data, zone, new Point(5, 5), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        zone.RaiseEvent(args); Dispatcher.UIThread.RunJobs(); return args;
    }
    public static DataTransfer Files(params IStorageItem[] items)
    {
        var transfer = new DataTransfer(); foreach (var item in items) transfer.Add(DataTransferItem.CreateFile(item)); return transfer;
    }
    public static void Save(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("未取得渲染帧。");
        image.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    public static IStorageFile File(Window window, string path)
    {
        var task = window.StorageProvider.TryGetFileFromPathAsync(path); Pump(task);
        return task.Result ?? throw new FileNotFoundException("本地存储项目不存在", path);
    }
    public static IStorageFolder Folder(Window window, string path)
    {
        var task = window.StorageProvider.TryGetFolderFromPathAsync(path); Pump(task);
        return task.Result ?? throw new DirectoryNotFoundException(path);
    }
    public static void Pick(Window window, SingleMuxView view, ScriptedInteractions interactions, string button, params IStorageItem[] items)
    {
        interactions.NextPick = items; Click(window, view, button); Pump(view.InputTask);
    }
    public static MenuItem FindMenuItem(SingleMuxView view, string buttonName, Func<MenuItem, bool> match) =>
        ((MenuFlyout)view.FindControl<Button>(buttonName)!.Flyout!).Items.OfType<MenuItem>().First(match);
    // 菜单项属弹出层，headless 下无法用键盘点击；以控件自身的 Click 事件驱动真实处理程序。
    public static void InvokeMenu(SingleMuxView view, string buttonName, Func<MenuItem, bool> match)
    {
        FindMenuItem(view, buttonName, match).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    public static void AddAss(SingleMuxView view, ScriptedInteractions interactions, params IStorageItem[] items)
    {
        interactions.NextPick = items;
        InvokeMenu(view, "AddTrackButton", item => item.Header?.ToString()!.Contains("ASS") == true);
        Pump(view.InputTask);
    }
}

internal sealed class ScriptedInteractions : ISingleMuxInteractions
{
    public IReadOnlyList<IStorageItem> NextPick { get; set; } = [];
    public bool Confirm { get; set; } = true;
    public bool CancelAssignment { get; set; }
    public int PickCount { get; private set; }
    public int ConfirmCount { get; private set; }
    public int AssignmentCount { get; private set; }
    public Func<Task<IReadOnlyList<IStorageItem>>>? PickOverride { get; set; }
    public Func<Task<IReadOnlyList<SubtitleAssignment>?>>? AssignmentOverride { get; set; }
    public Func<Window, SingleMuxViewModel, IReadOnlyList<InputCandidate>, SubtitleSlot?, Task<IReadOnlyList<SubtitleAssignment>?>>? AssignmentFlow { get; set; }
    public IReadOnlyList<SubtitleSlot>? Slots { get; set; }
    public string? CopiedText { get; private set; }
    public string? OpenedPath { get; private set; }
    public bool FailCopy { get; set; }
    public bool FailOpen { get; set; }
    public Task<IReadOnlyList<IStorageItem>> PickAsync(TopLevel owner, InputOperation operation) { PickCount++; return PickOverride?.Invoke() ?? Task.FromResult(NextPick); }
    public Task<bool> ConfirmVideoAsync(Window owner, string currentPath, string candidatePath) { ConfirmCount++; return Task.FromResult(Confirm); }
    public Task<IReadOnlyList<SubtitleAssignment>?> AssignSubtitlesAsync(Window owner, SingleMuxViewModel model, IReadOnlyList<InputCandidate> candidates, SubtitleSlot? fixedSlot)
    {
        AssignmentCount++;
        if (AssignmentFlow is not null) return AssignmentFlow(owner, model, candidates, fixedSlot);
        if (AssignmentOverride is not null) return AssignmentOverride();
        return Task.FromResult<IReadOnlyList<SubtitleAssignment>?>(CancelAssignment ? null : candidates.Select((c, i) => new SubtitleAssignment(fixedSlot ?? Slots?[i] ?? (SubtitleSlot)i, c)).ToArray());
    }
    public Task CopyAsync(TopLevel owner, string text) { if (FailCopy) throw new IOException("受控剪贴板不可用"); CopiedText = text; return Task.CompletedTask; }
    public void OpenLocation(string path) { if (FailOpen) throw new IOException("受控资源管理器不可用"); OpenedPath = path; }
}
