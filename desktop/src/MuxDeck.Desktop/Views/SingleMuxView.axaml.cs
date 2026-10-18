using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class SingleMuxView : UserControl
{
    // 单任务视图拥有完整客户区；浮层、拖入与焦点仍在页面宿主统一协调。
    public static readonly StyledProperty<bool> ShowDeveloperLogProperty = AvaloniaProperty.Register<SingleMuxView, bool>(nameof(ShowDeveloperLog), DefaultDeveloperMode);
    private static bool DefaultDeveloperMode => Environment.GetEnvironmentVariable("MUX_DECK_DEV_LOG") == "1" ||
#if DEBUG
        true;
#else
        false;
#endif
    public bool ShowDeveloperLog { get => GetValue(ShowDeveloperLogProperty); set => SetValue(ShowDeveloperLogProperty, value); }
    public ISingleMuxInteractions Interactions { get; set; } = new SingleMuxInteractions();
    public Task InputTask { get; private set; } = Task.CompletedTask;
    private bool acceptingInput;
    private Control? inspectorTrigger;
    private string? inspectorRowId;
    private SingleMuxViewModel? observedModel;
    private TopLevel? keyboardRoot;
    private SingleMuxViewModel? Model => DataContext as SingleMuxViewModel;
    public static readonly StyledProperty<TrackColumnLayout> ColumnLayoutProperty = AvaloniaProperty.Register<SingleMuxView, TrackColumnLayout>(nameof(ColumnLayout));
    public TrackColumnLayout ColumnLayout { get => GetValue(ColumnLayoutProperty); set => SetValue(ColumnLayoutProperty, value); }
    public SingleMuxView()
    {
        ColumnLayout = new();
        InitializeComponent();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) ClearDropFeedback(); };
        DataContextChanged += (_, _) => ObserveModel();
        LayoutUpdated += (_, _) => { UpdateColumnViewport(); UpdateResultScrollSpace(); };
        TrackListScroll.ScrollChanged += (_, _) => SyncHorizontalScroll(TrackListScroll, TrackHeaderScroll);
        TrackHeaderScroll.ScrollChanged += (_, _) => SyncHorizontalScroll(TrackHeaderScroll, TrackListScroll);
        DetachedFromVisualTree += (_, _) =>
        {
            if (observedModel is not null) observedModel.PropertyChanged -= ModelChanged;
            observedModel = null;
            keyboardRoot?.RemoveHandler(KeyDownEvent, HandleOverlayTab);
            if (keyboardRoot is not null) DetachWindowDrop(keyboardRoot);
            keyboardRoot = null;
            ClearDropFeedback();
        };
        AttachedToVisualTree += (_, _) =>
        {
            ObserveModel();
            keyboardRoot = TopLevel.GetTopLevel(this);
            // 侧栏不在本视图内，窗口级接线才能保持非模态面板的双向导航。
            keyboardRoot?.AddHandler(KeyDownEvent, HandleOverlayTab, RoutingStrategies.Tunnel);
            if (keyboardRoot is not null) AttachWindowDrop(keyboardRoot);
        };
    }
    private void ObserveModel()
    {
        if (ReferenceEquals(observedModel, Model)) return;
        observedModel?.DropFeedback.Clear();
        if (observedModel is not null) observedModel.PropertyChanged -= ModelChanged;
        observedModel = Model;
        if (observedModel is not null) ColumnLayout = observedModel.TrackColumns;
        if (observedModel is not null) observedModel.PropertyChanged += ModelChanged;
    }
    private void UpdateColumnViewport()
    {
        if (TrackListScroll.Viewport.Width <= 0) return;
        ColumnLayout.SetAvailableWidth(TrackListScroll.Viewport.Width);
        if (!double.IsFinite(TrackHeaderScroll.Width) || Math.Abs(TrackHeaderScroll.Width - TrackListScroll.Viewport.Width) > 0.5)
            TrackHeaderScroll.Width = TrackListScroll.Viewport.Width;
    }
    private static void SyncHorizontalScroll(ScrollViewer from, ScrollViewer to)
    {
        if (Math.Abs(from.Offset.X - to.Offset.X) > 0.5) to.Offset = new Vector(from.Offset.X, to.Offset.Y);
    }
    private async void ColumnsResized(object? sender, EventArgs e) => await PersistColumnsAsync();
    private async void ResetColumns(object? sender, RoutedEventArgs e) { ColumnLayout.Reset(); await PersistColumnsAsync(); }
    private async Task PersistColumnsAsync()
    {
        try { await ColumnLayout.PersistAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Model?.ReportInteractionMessage("列宽保存失败；本次调整仍有效：" + ex.Message, NotificationKind.Warning); }
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SingleMuxViewModel.CanEdit) && Model?.CanEdit != true) ClearDropFeedback();
        if (args.PropertyName is nameof(SingleMuxViewModel.IsTrackInspectorOpen) or nameof(SingleMuxViewModel.IsResultInspectorOpen)) UpdateOverlayBlocking();
    }
    // 被 Overlay 完全遮住的区域退出命中测试，避免点击/拖入穿透到不可见控件；清单保持可用以便切换可见轨道。
    private void UpdateOverlayBlocking()
    {
        var trackOpen = Model?.IsTrackInspectorOpen == true;
        var resultOpen = Model?.IsResultInspectorOpen == true;
        VideoDropZone.IsHitTestVisible = !trackOpen;
        FontsDropZone.IsHitTestVisible = !trackOpen && !resultOpen;
        UpdateResultScrollSpace();
    }
    private void UpdateResultScrollSpace()
    {
        // 只增加清单内部滚动余量，主布局不变；最后一行也能滚到结果面板上方。
        var height = Model?.IsResultInspectorOpen == true && ResultInspector.TranslatePoint(default, TrackListScroll) is { } panelPoint
            ? Math.Max(0, TrackListScroll.Viewport.Height - Math.Max(0, panelPoint.Y)) : 0;
        if (Math.Abs(ResultScrollSpace.Height - height) > 0.5) ResultScrollSpace.Height = height;
    }
    public static IReadOnlyList<InputCandidate> ReadStorageItems(IEnumerable<IStorageItem> items) => items.Select(item =>
        item.TryGetLocalPath() is { } path && item is IStorageFile or IStorageFolder
            ? new InputCandidate(path, item is IStorageFolder ? InputCandidateKind.LocalDirectory : InputCandidateKind.LocalFile)
            : new InputCandidate(item.Path.ToString(), InputCandidateKind.NonLocal)).ToArray();
    public static IReadOnlyList<InputCandidate> ReadTransfer(IDataTransfer transfer)
    {
        // Windows 可同时提供文件与文本表示。只读取文件集合，不把伴随格式误判成混入的 URL。
        var files = transfer.TryGetFiles()?.ToArray() ?? [];
        return files.Length > 0 ? ReadStorageItems(files) : [new(transfer.TryGetText() ?? "", InputCandidateKind.NonLocal)];
    }
    private static string? Preflight(InputOperation operation, IReadOnlyList<InputCandidate> candidates)
    {
        if (candidates.Any(c => c.Kind == InputCandidateKind.NonLocal)) return "仅接受系统本地文件或文件夹，URL / 文本路径不受支持。";
        if (candidates.Count < 1 || candidates.Count > (operation == InputOperation.Subtitles ? 2 : 1)) return operation == InputOperation.Subtitles ? "一次接纳 1–2 个 ASS。" : "这里只接纳单个视频或单个文件夹。";
        var folder = operation is InputOperation.FontsDirectory or InputOperation.OutputDirectory;
        if (candidates.Any(c => c.Kind != (folder ? InputCandidateKind.LocalDirectory : InputCandidateKind.LocalFile))) return folder ? "请拖入单个本地文件夹。" : "请提供本地文件，不能混合文件夹。";
        foreach (var candidate in candidates)
        {
            if (!Path.IsPathFullyQualified(candidate.Path) || (folder ? !Directory.Exists(candidate.Path) : !File.Exists(candidate.Path))) return "路径不存在或不是完整的本地路径。";
            var extension = Path.GetExtension(candidate.Path);
            if (!folder && (operation == InputOperation.Video ? !extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) : !extension.Equals(".ass", StringComparison.OrdinalIgnoreCase))) return operation == InputOperation.Video ? "视频仅支持 MKV / MP4。" : "外加内容仅支持 ASS。";
        }
        if (operation == InputOperation.Subtitles && candidates.Select(c => Path.GetFullPath(c.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Count) return "同一 ASS 不能重复分配。";
        return null;
    }
    private void PickVideo(object? sender, RoutedEventArgs args) => Pick(InputOperation.Video);
    private async void ReprobeVideo(object? sender, RoutedEventArgs args)
    {
        if (Model is not { CanEdit: true } model) return;
        try { await model.ProbeVideoAsync(); }
        catch (Exception ex) { model.ReportInteractionMessage("重新探测失败：" + ex.Message); }
    }
    private void PickAss(object? sender, RoutedEventArgs args) => PickMedia(InputOperation.Subtitles);
    private void PickAudio(object? sender, RoutedEventArgs args) => PickMedia(InputOperation.Audio);
    private void PickFonts(object? sender, RoutedEventArgs args) => Pick(InputOperation.FontsDirectory);
    private void PickOutput(object? sender, RoutedEventArgs args) => Pick(InputOperation.OutputDirectory);
    private void Pick(InputOperation operation, SubtitleSlot? fixedSlot = null) => BeginInput(operation, async () =>
        ReadStorageItems(await Interactions.PickAsync(TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("窗口不可用。"), operation)), fixedSlot);
    private void BeginInput(InputOperation operation, Func<Task<IReadOnlyList<InputCandidate>>> gather, SubtitleSlot? fixedSlot = null)
    {
        if (Model is not { } model) return;
        if (acceptingInput || !model.CanEdit) { model.ReportInteractionMessage("正在处理输入或任务，当前不能接纳新的输入。"); ClearDropFeedback(); return; }
        acceptingInput = true;
        InputTask = ProcessInputAsync(model, operation, model.DraftRevision, gather, fixedSlot);
    }
    private async Task ProcessInputAsync(SingleMuxViewModel model, InputOperation operation, long revision, Func<Task<IReadOnlyList<InputCandidate>>> gather, SubtitleSlot? fixedSlot)
    {
        bool StillEditable()
        {
            if (!ReferenceEquals(Model, model) || !model.CanEdit) { model.ReportInteractionMessage("操作返回时当前任务已锁定，原输入保持。"); return false; }
            if (revision != model.DraftRevision) { model.ReportInteractionMessage("等待期间草稿已变化，请重新选择或分配；原输入保持。"); return false; }
            return true;
        }
        try
        {
            var candidates = await gather();
            if (candidates.Count == 0) return;
            if (!StillEditable()) return;
            if (Preflight(operation, candidates) is { } error) { model.ReportInteractionMessage(error); return; }
            var owner = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("窗口不可用。");
            InputAcceptanceResult prepared;
            var confirmed = false;
            if (operation == InputOperation.Subtitles)
            {
                if (fixedSlot is not null && candidates.Count != 1) { model.ReportInteractionMessage("同槽更换一次只能选择一个 ASS。"); return; }
                var assignments = await Interactions.AssignSubtitlesAsync(owner, model, candidates, fixedSlot);
                if (assignments is null) return;
                if (!StillEditable()) return;
                prepared = model.PrepareSubtitleInputs(assignments);
                confirmed = true;
            }
            else
            {
                prepared = operation switch { InputOperation.Video => model.PrepareVideoInput(candidates), InputOperation.FontsDirectory => model.PrepareFontsDirectory(candidates), _ => model.PrepareOutputDirectory(candidates) };
                if (prepared.Succeeded && prepared.Plan!.RequiresReplacementConfirmation)
                {
                    confirmed = await Interactions.ConfirmVideoAsync(owner, model.VideoPath, prepared.Plan.Candidates[0].Path);
                    if (!confirmed) return;
                }
            }
            if (!prepared.Succeeded || !StillEditable()) return;
            model.CommitInput(prepared.Plan!, confirmed);
        }
        catch (Exception ex) { model.ReportInteractionMessage("输入操作失败：" + ex.Message); }
        finally { acceptingInput = false; ClearDropFeedback(); }
    }
    private void ShowTrack(object? sender, RoutedEventArgs args)
    {
        if (Model is not { } model || (sender as Control)?.DataContext is not TrackRowViewModel row) return;
        inspectorTrigger = sender as Control;
        inspectorRowId = row.Id;
        model.SelectedTrack = row;
        model.OpenTrackInspector();
        FocusPanel(TrackInspector);
    }
    private void CloseTrackInspector(object? sender, RoutedEventArgs args) => CloseInspectors();
    private void CloseResultInspector(object? sender, RoutedEventArgs args) { Model?.CloseResultInspector(); RestoreInspectorFocus(); }
    private void CloseInspectors() { Model?.CloseInspectors(); RestoreInspectorFocus(); }
    private void FocusPanel(Control panel) => Dispatcher.UIThread.Post(() => panel.Focus(), DispatcherPriority.Loaded);
    private void RestoreInspectorFocus() => Dispatcher.UIThread.Post(() =>
    {
        // 行控件会随清单重建而更换，必须按当前视觉树里的行/入口解析，不能沿用已脱离的旧控件。
        FocusFirstAvailable(CurrentRowButton(), LiveTrigger(), AddTrackButton, StartButton);
    }, DispatcherPriority.Loaded);
    private Button? CurrentRowButton() => RowButton(Model?.SelectedTrack?.Id) ?? RowButton(inspectorRowId);
    private Button? RowButton(string? id) => id is null ? null : this.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(b => b.Classes.Contains("row-select") && b.DataContext is TrackRowViewModel row && row.Id == id);
    private Control? LiveTrigger() => inspectorTrigger is { } trigger && TopLevel.GetTopLevel(trigger) is not null && trigger.IsEffectivelyVisible && trigger.IsEffectivelyEnabled ? trigger : null;
    private static void FocusFirstAvailable(params Control?[] candidates)
    {
        foreach (var candidate in candidates)
            if (candidate is not null && TopLevel.GetTopLevel(candidate) is not null && candidate.IsEffectivelyVisible && candidate.IsEffectivelyEnabled)
            {
                candidate.BringIntoView();
                if (candidate.Focus()) return;
            }
    }
    private void ShowResult(object? sender, RoutedEventArgs args)
    {
        MuxTabs.SelectedIndex = 0;
        if (Model is not { } model) return;
        inspectorTrigger = sender as Control ?? ShowResultButton;
        inspectorRowId = null;
        model.ToggleResultInspector();
        if (model.IsResultInspectorOpen) FocusPanel(ResultInspector);
        else RestoreInspectorFocus();
    }
    // 非模态 Overlay 的键盘规则：被面板遮挡的复合区域（视频摘要、设置）整体退出 Tab 导航，
    // 可见轨道清单保持可用；结果面板下被完全盖住的行也不参与导航。指针规则与此一致。
    private bool IsTabReachable(Control control)
    {
        if (Model is not { } model) return true;
        var overlay = model.IsTrackInspectorOpen ? TrackInspector : model.IsResultInspectorOpen ? ResultInspector : null;
        if (overlay is null) return true;
        // 面板自己的控件是可见操作，不能把它们当作被面板覆盖的底层控件。
        if (IsWithin(control, overlay)) return true;
        if (model.IsTrackInspectorOpen && (IsWithin(control, VideoDropZone) || IsWithin(control, FontsDropZone))) return false;
        if (model.IsResultInspectorOpen && IsWithin(control, FontsDropZone)) return false;
        var bounds = VisibleBounds(control);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        var covered = bounds.Intersect(VisibleBounds(overlay));
        return covered.Width * covered.Height < bounds.Width * bounds.Height * 0.98;
    }
    private static bool IsWithin(Visual child, Visual ancestor)
    {
        for (Visual? current = child; current is not null; current = current.GetVisualParent()) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private Rect VisibleBounds(Control control)
    {
        if (keyboardRoot is null || control.TranslatePoint(default, keyboardRoot) is not { } point) return default;
        var bounds = new Rect(point, control.Bounds.Size).Intersect(new Rect(keyboardRoot.Bounds.Size));
        // 清单裁剪外的行不能按未裁剪坐标误判为可达；面板自身滚动由 BringIntoView 处理。
        for (var parent = control.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
            if ((parent.ClipToBounds || parent is ScrollViewer) && parent.TranslatePoint(default, keyboardRoot) is { } parentPoint)
                bounds = bounds.Intersect(new Rect(parentPoint, parent.Bounds.Size));
        return bounds;
    }
    private List<Control> TabStops() => (keyboardRoot?.GetVisualDescendants() ?? []).OfType<Control>()
        .Where(c => !ReferenceEquals(c, this) && c is not ScrollViewer && c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && IsTabReachable(c))
        .OrderBy(c => c.TabIndex)
        .ToList();
    private void HandleOverlayTab(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !IsEffectivelyVisible || MuxTabs.SelectedIndex != 0 || Model?.HasOpenInspector != true) return;
        MoveTabFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }
    private void MoveTabFocus(bool backward)
    {
        var stops = TabStops();
        if (stops.Count == 0) return;
        var current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var index = current is null ? -1 : stops.IndexOf(current);
        var next = index < 0 ? (backward ? stops.Count - 1 : 0) : (index + (backward ? -1 : 1) + stops.Count) % stops.Count;
        stops[next].BringIntoView();
        stops[next].Focus();
    }
    private List<Button> RowButtons() => this.GetVisualDescendants().OfType<Button>()
        .Where(b => b.Classes.Contains("row-select") && b.DataContext is TrackRowViewModel && b.IsEffectivelyVisible)
        .ToList();
    private void MoveRowFocus(Control focused, bool down)
    {
        if (focused is not Button button || button.DataContext is not TrackRowViewModel row) return;
        var rows = RowButtons();
        var index = rows.FindIndex(candidate => ReferenceEquals(candidate.DataContext, row));
        if (index < 0) return;
        for (var i = index + (down ? 1 : -1); i >= 0 && i < rows.Count; i += down ? 1 : -1)
        {
            var target = rows[i];
            if (!target.IsEffectivelyEnabled) continue;
            // 方向键能滚入视口外的下一行，不能复用 Tab 仅枚举当前可见控件的过滤。
            if (Model?.IsResultInspectorOpen == true && target.TranslatePoint(default, TrackListScroll) is { } point &&
                ResultInspector.TranslatePoint(default, TrackListScroll) is { } panelPoint)
            {
                var exposedHeight = Math.Min(TrackListScroll.Viewport.Height, panelPoint.Y);
                if (exposedHeight <= 0) return;
                var rowOffset = TrackListScroll.Offset.Y + point.Y;
                target.Focus();
                // 结果面板盖住清单下半部，将目标放到仍露出的上缘，而非滚到面板后面。
                if (point.Y < 0 || point.Y + target.Bounds.Height > exposedHeight)
                    TrackListScroll.Offset = new Vector(TrackListScroll.Offset.X, rowOffset);
            }
            else { target.Focus(); target.BringIntoView(); }
            // 面板已开时原位跟随该轨道；方向导航不改草稿或包含状态。
            if (Model is { HasOpenInspector: true } model && target.DataContext is TrackRowViewModel targetRow) model.SelectedTrack = targetRow;
            return;
        }
    }
    private void ReplaceExternal(object? sender, RoutedEventArgs args)
    {
        if (Model?.SelectedTrack is { IsEditable: true } row)
        {
            if (row.Slot is { } slot) Pick(InputOperation.Subtitles, slot);
            else PickMedia(row.IsAudio ? InputOperation.Audio : InputOperation.Subtitles, row);
        }
    }
    private void RemoveExternal(object? sender, RoutedEventArgs args)
    {
        if (Model?.SelectedTrack is not { IsEditable: true } row) return;
        Model.RemoveExternalTrack(row);
    }
    private async void OpenAttachments(object? sender, RoutedEventArgs args)
    {
        if (Model is not { } model || model.SourceAttachments.Count == 0) return;
        try
        {
            var dialog = SingleMuxInteractions.CreateDialog("源附件清单", 640);
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = $"源附件（{model.SourceAttachmentCount}）· 本次不保留；成品只包含本次子集字体。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            foreach (var attachment in model.SourceAttachments)
            {
                var text = new TextBlock { Text = $"{attachment.FileName} · {attachment.ContentType} · {attachment.Size:N0} 字节", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                text.Classes.Add("caption");
                body.Children.Add(text);
            }
            dialog.Content = new ScrollViewer { Content = body, MaxHeight = 460 };
            if (TopLevel.GetTopLevel(this) is Window owner) await dialog.ShowDialog(owner);
        }
        catch (Exception ex) { model.ReportInteractionMessage("查看附件失败：" + ex.Message); }
    }
    private async void CopyOutputPath(object? sender, RoutedEventArgs args) => await CopyTextAsync(Model?.OutputPath);
    private async void CopyDiagnostics(object? sender, RoutedEventArgs args)
    {
        if (Model?.Result is { } result) await CopyTextAsync(Model.ResultLabel + "\n" + JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        else Model?.ReportInteractionMessage("尚无任务结果可复制。", NotificationKind.Information);
    }
    private async Task CopyTextAsync(string? text)
    {
        if (Model is not { } model) return;
        try { if (string.IsNullOrWhiteSpace(text)) { model.ReportInteractionMessage("当前没有可复制的路径或诊断。", NotificationKind.Information); return; } await Interactions.CopyAsync(TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("窗口不可用。"), text); model.ReportInteractionMessage("已复制。", NotificationKind.Success); }
        catch (Exception ex) { model.ReportInteractionMessage("复制失败：" + ex.Message); }
    }
    private void OpenOutput(object? sender, RoutedEventArgs args) => OpenPath(Model?.OutputPath, "已打开文件位置。");
    private void OpenOutputDirectory(object? sender, RoutedEventArgs args) => OpenPath(Model?.OutputDirectory, "已打开输出目录。");
    private void OpenFonts(object? sender, RoutedEventArgs args) => OpenPath(Model?.FontsDirectory, "已打开字体目录。");
    private void ClearFonts(object? sender, RoutedEventArgs args)
    {
        if (Model is not { CanEdit: true } model) return;
        model.FontsDirectory = ""; model.ReportInteractionMessage("已清空字体目录。", NotificationKind.Information);
    }
    private void ClearOutput(object? sender, RoutedEventArgs args)
    {
        if (Model is not { CanEdit: true } model) return;
        model.OutputDirectory = ""; model.ReportInteractionMessage("已清空输出目录。", NotificationKind.Information);
    }
    private void OpenLog(object? sender, RoutedEventArgs args) => OpenPath(Model?.LogPath, "已打开文件位置。");
    private void OpenPath(string? path, string successMessage)
    {
        try { if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("目标不存在或已经移除。"); Interactions.OpenLocation(path); Model?.ReportInteractionMessage(successMessage, NotificationKind.Success); }
        catch (Exception ex) { Model?.ReportInteractionMessage("打开失败：" + ex.Message); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Model is { } model)
        {
            // Esc 关闭当前 Overlay；弹出菜单/下拉不在本控件内，先由它们自己处理。
            if (e.Key == Key.Escape && model.HasOpenInspector) { model.CloseInspectors(); RestoreInspectorFocus(); e.Handled = true; }
            // 轨道行获焦时用普通方向键在可见行间移动；文本/下拉内仍是控件自身语义。
            else if (e.Key is Key.Down or Key.Up && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focused && focused is Button { } focusedButton && focusedButton.Classes.Contains("row-select")) { MoveRowFocus(focusedButton, e.Key == Key.Down); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }
}
