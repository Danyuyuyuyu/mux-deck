using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using static UiInputTestSupport;

internal static class InputFeedbackChecks
{
    private sealed record CheckResult(string Name, bool Passed);
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "input-feedback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<CheckResult>(); Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                void Check(bool result, string name) { checks.Add(new(name, result)); Console.WriteLine((result ? "PASS: " : "FAIL: ") + name); if (!result) throw new InvalidOperationException(name); }
                Verify(root, Check);
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        System.IO.File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Checks = checks, Failure = failure?.ToString(), ArtifactRoot = root, Scope = "窗口事件、实际控件、计时及渲染；非 Explorer 原生拖放" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Input feedback: {checks.Count(c => c.Passed)}/{checks.Count}; Report: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Verify(string root, Action<bool, string> check)
    {
        var vm = new MainWindowViewModel(Workspace.Discover());
        var window = new MainWindow { DataContext = vm };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<SingleMuxView>("SingleView")!;
        var notice = window.FindControl<InteractionNotice>("InputNotice")!;
        var overlay = window.FindControl<Grid>("MediaDropOverlay")!;
        var single = vm.Single;
        var fonts = Path.Combine(root, "fonts"); Directory.CreateDirectory(fonts);
        using var data = Files(Folder(window, fonts));
        var navigation = window.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "单个封装");
        var zone = view.FindControl<Border>("VideoDropZone")!;
        DragEventArgs At(Control target, RoutedEvent<DragEventArgs> evt, IDataTransfer transfer, Point? point = null)
        {
            var args = new DragEventArgs(evt, transfer, target, point ?? new Point(5, 5), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
            target.RaiseEvent(args); Dispatcher.UIThread.RunJobs(); return args;
        }
        void Snapshot(string name) => Save(window, Path.Combine(root, name + ".png"));
        try
        {
            var revision = single.DraftRevision;
            var over = At(navigation, DragDrop.DragEnterEvent, data);
            check(over.DragEffects == DragDropEffects.Copy && overlay.IsVisible && single.DraftRevision == revision, "侧栏拖入触发统一遮罩，悬停不写草稿");
            check(!overlay.IsHitTestVisible && overlay.Bounds.Size == window.ClientSize && overlay.TranslatePoint(default, window) == default(Point), "遮罩覆盖整个客户区且不截获指针");
            Snapshot("overlay-1280");
            At(navigation, DragDrop.DragLeaveEvent, data); At(zone, DragDrop.DragEnterEvent, data);
            check(overlay.IsVisible && !zone.Classes.Contains("accepting") && view.FindControl<TextBlock>("VideoDropHint") is null, "跨侧栏和子控件保持遮罩，无区域高亮和插入文案");
            At(zone, DragDrop.DragLeaveEvent, data); UntilWithTimers(() => !overlay.IsVisible, 2);
            check(!overlay.IsVisible, "子控件离开且未重新进入时清理遮罩");
            At(zone, DragDrop.DragOverEvent, data); At(window, DragDrop.DragLeaveEvent, data, new Point(-1, -1));
            check(!overlay.IsVisible, "离开窗口立即清理遮罩");
            At(zone, DragDrop.DragOverEvent, data); window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            check(!overlay.IsVisible, "Esc 取消拖入清理遮罩");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText("https://example.invalid/video.mkv"));
            var rejected = At(zone, DragDrop.DragOverEvent, text);
            check(rejected.DragEffects == DragDropEffects.None && overlay.IsVisible && !single.DropFeedback.CanAccept && single.DropFeedback.Description.Contains("文本"), "不支持输入显示原因和禁止落下状态");
            At(navigation, DragDrop.DropEvent, data); Pump(view.InputTask);
            check(!overlay.IsVisible && single.FontsDirectory == fonts && single.Notification.IsSuccess && !single.Notification.Message.Contains("0 条"), "从侧栏释放设置字体目录，通知省略零轨道");
            var status = view.FindControl<TextBlock>("CurrentState")!.Text;
            check(notice.IsVisible && view.FindControl<TextBlock>("InteractionMessage") is null && status == single.ActionBarSummary, "临时反馈位于右上角，底部任务摘要仍保留");
            Snapshot("success-1280");
            var firstRevision = single.Notification.Revision;
            single.ReportInteractionMessage(single.Notification.Message, NotificationKind.Success);
            check(single.Notification.Revision == firstRevision, "连续相同通知不重新计时");
            UntilWithTimers(() => !notice.IsVisible, 7);
            check(!notice.IsVisible && single.DraftRevision == revision + 1, "成功通知五秒后自动隐藏，不更改草稿");
            single.ReportInteractionMessage("已清空示例目录。", NotificationKind.Information); UntilWithTimers(() => !notice.IsVisible, 6);
            check(!notice.IsVisible, "普通操作反馈自动消失");
            single.ReportInteractionMessage("正在识别…", NotificationKind.Busy);
            check(notice.IsVisible && !notice.FindControl<Button>("DismissNoticeButton")!.IsVisible && single.Notification.Lifetime is null, "处理中保持可见且不提供误关闭入口");
            single.ReportInteractionMessage("故意使用成功字样的错误消息。", NotificationKind.Error);
            single.Notification.Dismiss(firstRevision);
            check(notice.IsVisible && single.Notification.IsError && single.Notification.Message.Contains("成功") && single.Notification.Lifetime is null, "显式错误类型不解析文案，旧计时不能关闭新错误");
            Snapshot("error-1280");
            Click(window, notice.FindControl<Button>("DismissNoticeButton")!);
            check(!notice.IsVisible && view.FindControl<TextBlock>("CurrentState")!.Text == status, "错误可通过真实按钮关闭，底部状态保持");
            single.ReportInteractionMessage("成功后即刻出现的错误", NotificationKind.Success);
            single.ReportInteractionMessage("新的错误不能被旧成功计时关闭", NotificationKind.Error);
            var wait = System.Diagnostics.Stopwatch.StartNew();
            UntilWithTimers(() => wait.Elapsed >= TimeSpan.FromSeconds(5.3), 7);
            check(notice.IsVisible && single.Notification.IsError, "实际旧成功计时到期后新错误仍可见");
            var ass = Path.Combine(root, "example.ass"); System.IO.File.WriteAllText(ass, "fixture"); single.ScSubtitle = ass;
            single.SelectedTrack = single.ScTrack; single.OpenTrackInspector(); Dispatcher.UIThread.RunJobs();
            var inspector = view.FindControl<Border>("TrackInspector")!;
            rejected = At(inspector, DragDrop.DragOverEvent, data);
            check(rejected.DragEffects == DragDropEffects.None && !single.DropFeedback.CanAccept, "详情面板拖入拒绝，遮罩不绕过原遮挡规则");
            foreach (var (width, height) in new[] { (1280d, 900d), (1040d, 760d) })
            {
                window.Width = width; window.Height = height; Dispatcher.UIThread.RunJobs();
                single.ReportInteractionMessage(new string('长', 120) + " · 完整错误信息", NotificationKind.Error); Dispatcher.UIThread.RunJobs();
                var location = notice.TranslatePoint(default, window)!.Value;
                var inspectorLocation = inspector.TranslatePoint(default, window)!.Value;
                check(location.X + notice.Bounds.Width <= inspectorLocation.X - 10 && location.Y + notice.Bounds.Height <= 80 && notice.Bounds.Width <= 300, $"{width} 宽通知避让属性栏且保留页签空间");
                check(ToolTip.GetTip(notice.GetVisualDescendants().OfType<Border>().First())?.ToString() == single.Notification.Message, $"{width} 宽长通知全文可从 Tooltip 获取");
                single.DropFeedback.Clear(); Snapshot("long-inspector-" + width);
            }
            single.CloseInspectors(); At(zone, DragDrop.DragOverEvent, data); vm.SelectedPage = 1; Dispatcher.UIThread.RunJobs();
            check(!overlay.IsVisible && At(navigation, DragDrop.DragOverEvent, data).DragEffects == DragDropEffects.None, "切换页面清理遮罩且其他页面不接纳输入");
            vm.SelectedPage = 0; Dispatcher.UIThread.RunJobs();
            var modal = new Window { Width = 250, Height = 120 }; var dialogTask = modal.ShowDialog(window); Dispatcher.UIThread.RunJobs();
            rejected = At(zone, DragDrop.DragOverEvent, data); var draft = single.DraftRevision; At(zone, DragDrop.DropEvent, data);
            check(rejected.DragEffects == DragDropEffects.None && single.DraftRevision == draft, "真实确认窗口打开时底层不能接纳文件");
            modal.Close(); Pump(dialogTask);
            At(zone, DragDrop.DragOverEvent, data); Pump(single.CancelAndWaitAsync());
            check(!overlay.IsVisible && At(zone, DragDrop.DragOverEvent, data).DragEffects == DragDropEffects.None, "关闭等待清理遮罩并锁定拖入");
        }
        finally { Pump(single.CancelAndWaitAsync()); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
