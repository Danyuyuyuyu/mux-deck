using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class SingleMuxView
{
    private IDisposable? pendingDragLeave;
    private void AttachWindowDrop(TopLevel host)
    {
        DragDrop.SetAllowDrop(host, true);
        // 事件统一在窗口处理，包括导航；子区域不再拥有各自的高亮和接纳处理。
        host.AddHandler(DragDrop.DragEnterEvent, WindowDragOver, RoutingStrategies.Bubble, true);
        host.AddHandler(DragDrop.DragOverEvent, WindowDragOver, RoutingStrategies.Bubble, true);
        host.AddHandler(DragDrop.DragLeaveEvent, WindowDragLeave, RoutingStrategies.Bubble, true);
        host.AddHandler(DragDrop.DropEvent, WindowDrop, RoutingStrategies.Bubble, true);
        host.AddHandler(KeyDownEvent, ClearCancelledDrag, RoutingStrategies.Tunnel);
    }
    private void DetachWindowDrop(TopLevel host)
    {
        host.RemoveHandler(DragDrop.DragEnterEvent, WindowDragOver);
        host.RemoveHandler(DragDrop.DragOverEvent, WindowDragOver);
        host.RemoveHandler(DragDrop.DragLeaveEvent, WindowDragLeave);
        host.RemoveHandler(DragDrop.DropEvent, WindowDrop);
        host.RemoveHandler(KeyDownEvent, ClearCancelledDrag);
        ClearDropFeedback();
    }
    private bool DropIsBlocked(DragEventArgs args) =>
        keyboardRoot?.IsEnabled != true ||
        keyboardRoot is Window window && window.OwnedWindows.Any(child => child.IsVisible && child.IsDialog) ||
        TrackInspector.IsEffectivelyVisible && new Rect(TrackInspector.Bounds.Size).Contains(args.GetPosition(TrackInspector)) ||
        ResultInspector.IsEffectivelyVisible && new Rect(ResultInspector.Bounds.Size).Contains(args.GetPosition(ResultInspector));
    private string? DropError(DragEventArgs args, IReadOnlyList<InputCandidate> candidates)
    {
        if (!IsEffectivelyVisible) return "请先进入单个封装页面。";
        if (acceptingInput) return "正在处理输入，当前不能接纳新的输入。";
        if (Model?.CanEdit != true) return "当前任务已锁定，等待任务或清理结束。";
        if (DropIsBlocked(args)) return "详情面板或确认窗口不接纳文件，请拖到可见工作区。";
        return SingleMuxViewModel.MediaInputError(candidates);
    }
    private void WindowDragOver(object? sender, DragEventArgs args)
    {
        pendingDragLeave?.Dispose(); pendingDragLeave = null;
        args.Handled = true;
        if (!IsEffectivelyVisible) { args.DragEffects = DragDropEffects.None; ClearDropFeedback(); return; }
        try
        {
            var error = DropError(args, ReadTransfer(args.DataTransfer));
            args.DragEffects = error is null ? DragDropEffects.Copy : DragDropEffects.None;
            Model?.DropFeedback.Show(error is null, error ?? "");
        }
        catch (Exception ex) { args.DragEffects = DragDropEffects.None; Model?.DropFeedback.Show(false, "无法读取拖入数据：" + ex.Message); }
    }
    private void WindowDragLeave(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        pendingDragLeave?.Dispose();
        if (keyboardRoot is null || !new Rect(keyboardRoot.Bounds.Size).Contains(args.GetPosition(keyboardRoot))) { ClearDropFeedback(); return; }
        // 子控件切换会先 Leave 再 Enter，短暂延迟只用于合并这对事件，不让遮罩闪烁。
        pendingDragLeave = DispatcherTimer.RunOnce(ClearDropFeedback, TimeSpan.FromMilliseconds(70));
    }
    private void WindowDrop(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ClearDropFeedback();
        if (!IsEffectivelyVisible) { args.DragEffects = DragDropEffects.None; return; }
        try
        {
            var candidates = ReadTransfer(args.DataTransfer);
            if (DropError(args, candidates) is { } error)
            { args.DragEffects = DragDropEffects.None; Model?.ReportInteractionMessage(error); return; }
            // 同步返回 Copy，异步识别只改变草稿，不移动或删除原文件。
            args.DragEffects = DragDropEffects.Copy;
            BeginMediaInput(() => Task.FromResult(candidates));
        }
        catch (Exception ex) { args.DragEffects = DragDropEffects.None; Model?.ReportInteractionMessage("拖入失败：" + ex.Message); }
    }
    private void ClearCancelledDrag(object? sender, KeyEventArgs args) { if (args.Key == Key.Escape) ClearDropFeedback(); }
    private void ClearDropFeedback()
    {
        pendingDragLeave?.Dispose(); pendingDragLeave = null;
        Model?.DropFeedback.Clear();
    }
}
