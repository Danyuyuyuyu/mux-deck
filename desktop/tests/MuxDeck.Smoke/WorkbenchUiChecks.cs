using MuxDeck.Desktop.Composition;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Desktop;
using MuxDeck.Desktop.Preferences;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using static UiInputTestSupport;

internal static class WorkbenchUiChecks
{
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "workbench-ui", Guid.NewGuid().ToString("N"));
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
        System.IO.File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Checks = checks, Failure = failure?.ToString(), ArtifactRoot = root }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Workbench UI: {checks.Count(c => c.Passed)}/{checks.Count}; Report: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Verify(string root, Action<bool, string> check)
    {
        var preferencePath = Path.Combine(root, "preferences.json");
        var main = AppComposition.CreateMain(new Workspace(root), preferences: new UserPreferencesStore(preferencePath));
        var vm = main.Single; var layout = vm.TrackColumns;
        var window = new MainWindow { DataContext = main, Width = 1040, Height = 760 }; window.Show(); Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<SingleMuxView>("SingleView")!;
        try
        {
            check(view.GetVisualDescendants().OfType<TextBlock>().All(t => t.Text != "单个封装" && t.Text != "一个视频 · 字幕与字体 · MKV 成品"), "工作区无重复标题和说明");
            check(new[] { "VideoDropZone", "AssDropZone", "FontsDropZone", "StatusPanel" }.All(n => !view.FindControl<Border>(n)!.Classes.Contains("panel")), "四个大区不再使用卡片样式，命名交互区域仍存在");
            var headers = view.GetVisualDescendants().OfType<TrackColumnHeader>().OrderBy(h => h.Index).ToArray();
            check(headers.Length == 8 && string.Join("|", headers.Select(h => h.Title)) == "|ID|类型|语言|名称|默认|强制|编码", "表格列顺序完整且第一列表头空白");
            check(TrackColumnLayout.Columns.Count == 8 && layout.Widths.Select((w, i) => w >= TrackColumnLayout.Columns[i].MinimumWidth).All(v => v), "初始列宽满足每列最小值");
            vm.TcSubtitle = Path.Combine(root, "tc.ass"); vm.ScSubtitle = Path.Combine(root, "sc.ass"); Dispatcher.UIThread.RunJobs();
            check(vm.ExternalTracks[0] == vm.TcTrack && vm.TcTrack.DisplayId == "0" && vm.ExternalTracks[1] == vm.ScTrack && vm.ScTrack.DisplayId == "1", "TC 先添加时按添加顺序数字编号，不按 SC/TC 固定顺序展示");
            vm.TcSubtitle = Path.Combine(root, "tc-replaced.ass");
            check(vm.ExternalTracks[0] == vm.TcTrack && vm.TcTrack.DisplayId == "0" && vm.TcTrack.Id == "TC" && vm.ScTrack.Id == "SC", "同槽替换保留位置，内部槽位身份不变");
            vm.RemoveSubtitle(SubtitleSlot.Tc); vm.TcSubtitle = Path.Combine(root, "tc-again.ass"); Dispatcher.UIThread.RunJobs();
            check(vm.ExternalTracks[0] == vm.ScTrack && vm.ScTrack.DisplayId == "0" && vm.TcTrack.DisplayId == "1", "移除后编号重排，再添加排在末尾");
            vm.ScTrack.Name = ""; vm.ScTrack.IsForced = true; Dispatcher.UIThread.RunJobs();
            var rowButton = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("row-select") && ReferenceEquals(b.DataContext, vm.ScTrack));
            check(vm.ScTrack.DisplayName == "" && !rowButton.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "（未命名）"), "空名称留空，不以槽位或未命名文本填充");
            check(vm.ScTrack.DefaultMark == "✓" && vm.ScTrack.ForcedMark == "✓" && rowButton.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text == "✓") == 2, "默认与强制独立显示开启符号并实时绑定");
            vm.ScTrack.IsDefault = false; vm.ScTrack.IsForced = false; Dispatcher.UIThread.RunJobs();
            check(vm.ScTrack.DefaultMark == "" && vm.ScTrack.ForcedMark == "" && !rowButton.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "✓"), "关闭标记留空，详情编辑后列表同步");
            check(vm.ScTrack.LanguageName == "简体中文" && vm.ScTrack.Language == "zh-Hans" && LanguageDisplay.Name("und") == "未指定" && LanguageDisplay.Name("jpn") == "日语", "语言展示转换且原始代码保留");
            var unknown = new TrackRowViewModel { Id = "7", Type = "字幕", HasDefaultFlag = false, HasForcedFlag = false };
            check(unknown.DefaultMark == "" && unknown.ForcedMark == "" && unknown.DefaultTip.Contains("未声明") && unknown.ForcedTip.Contains("未声明"), "未声明字段不伪造开启，Tooltip 保留未知语义");
            var video = new TrackRowViewModel { Id = "0", Type = "视频", IsForced = true };
            check(video.ForcedMark == "" && video.ForcedTip.Contains("仅适用于字幕"), "视频强制值不被展示成字幕语义");

            var scroll = view.FindControl<ScrollViewer>("TrackListScroll")!; var headerScroll = view.FindControl<ScrollViewer>("TrackHeaderScroll")!;
            var revision = vm.DraftRevision;
            var grip = headers[2].FindControl<Border>("ResizeGrip")!;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            var before = layout.Widths[2];
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(75, 0)); window.MouseUp(point + new Vector(75, 0), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            check(Math.Abs(layout.Widths[2] - before - 75) < 1, "真实表头指针拖动修改列宽");
            check(grip.Cursor?.ToString().Contains("SizeWestEast") == true, "列边界使用水平调整光标");
            grip.Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); Dispatcher.UIThread.RunJobs();
            check(Math.Abs(layout.Widths[2] - before - 67) < 1, "列边界支持键盘调整");
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null); window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null); Dispatcher.UIThread.RunJobs();
            check(Math.Abs(layout.Widths[2] - TrackColumnLayout.Columns[2].DefaultWidth) < 0.1, "Home 恢复单列默认值");
            layout.Resize(2, -500); Dispatcher.UIThread.RunJobs();
            check(layout.Widths[2] == TrackColumnLayout.Columns[2].MinimumWidth, "缩小列宽不能突破最小值");
            layout.Resize(7, 520); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            check(scroll.Extent.Width > scroll.Viewport.Width && window.Bounds.Width == 1040, "窄窗口宽列仅在表格内部横向滚动");
            scroll.Offset = new Vector(100, scroll.Offset.Y); Dispatcher.UIThread.RunJobs();
            check(Math.Abs(scroll.Offset.X - headerScroll.Offset.X) < 0.5, "表头和数据水平偏移同步");
            var grids = view.GetVisualDescendants().OfType<TrackColumnsGrid>().ToArray();
            check(grids.All(g => g.ColumnDefinitions.Count == 8 && g.ColumnDefinitions.Select(c => c.Width.Value).SequenceEqual(layout.Widths)), "源行、外加行和表头共享全部列宽");
            var headerGrid = view.FindControl<TrackColumnsGrid>("TrackHeaderGrid")!;
            var cellGrid = rowButton.GetVisualDescendants().OfType<TrackColumnsGrid>().Single();
            check(Math.Abs(headerGrid.TranslatePoint(default, window)!.Value.X - cellGrid.TranslatePoint(default, window)!.Value.X) < 0.5, "水平滚动后表头与行起点对齐");
            var offsetBeforeNavigation = scroll.Offset.X;
            rowButton.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Dispatcher.UIThread.RunJobs();
            check(Math.Abs(scroll.Offset.X - offsetBeforeNavigation) < 0.5, "方向键切换轨道保留用户横向查看位置");
            check(vm.DraftRevision == revision, "调整列宽不修改业务草稿版本");
            Pump(layout.PersistAsync());
            var nextLayout = new TrackColumnLayout(new UserPreferencesStore(preferencePath));
            check(nextLayout.Widths[7] == 520 && nextLayout.Widths[2] == TrackColumnLayout.Columns[2].MinimumWidth, "独立配置实例恢复上次列宽，模拟重启");
            layout.Resize(7, 330); var save1 = layout.PersistAsync(); layout.Resize(7, 410); var save2 = layout.PersistAsync(); Pump(Task.WhenAll(save1, save2));
            check(new TrackColumnLayout(new UserPreferencesStore(preferencePath)).Widths[7] == 410, "连续保存串行提交，最新偏好不会被旧快照覆盖");
            var resetMenu = (MenuItem)((Border)headerGrid.Parent!).ContextMenu!.Items[0]!;
            resetMenu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); Pump(layout.FlushAsync()); Dispatcher.UIThread.RunJobs();
            check(new TrackColumnLayout(new UserPreferencesStore(preferencePath)).Widths[7] == TrackColumnLayout.Columns[7].DefaultWidth, "恢复默认会清除保存的用户覆盖值");
            System.IO.File.WriteAllText(preferencePath, "broken json");
            check(new TrackColumnLayout(new UserPreferencesStore(preferencePath)).Widths[7] == TrackColumnLayout.Columns[7].DefaultWidth, "损坏偏好回退初始布局");
            System.IO.File.WriteAllText(preferencePath, "{\"Version\":1,\"TrackColumnWidths\":{\"type\":-5,\"codec\":10000,\"unknown\":50}}");
            var clamped = new TrackColumnLayout(new UserPreferencesStore(preferencePath));
            check(clamped.Widths[2] == 80 && clamped.Widths[7] == 2000, "偏好加载限制非法宽度并忽略未知列");
            var badStore = new TrackColumnLayout(new UserPreferencesStore(root)); badStore.Resize(7, 345);
            try { Pump(badStore.PersistAsync()); throw new InvalidOperationException("应报告保存失败"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { check(badStore.Widths[7] == 345, "保存失败不撤销当前列宽"); }

            var fonts = Path.Combine(root, "fonts"); var output = Path.Combine(root, "output"); Directory.CreateDirectory(fonts); Directory.CreateDirectory(output);
            vm.FontsDirectory = fonts; vm.OutputDirectory = output;
            vm.VideoPath = Path.Combine(root, "missing.mkv");
            var interactions = new ScriptedInteractions(); view.Interactions = interactions; Dispatcher.UIThread.RunJobs();
            check(vm.CanStart, "目录清空前基本输入完整");
            InvokeMenu(view, "FontsMenuButton", i => Equals(i.Header, "打开目录"));
            check(interactions.OpenedPath == fonts, "菜单打开目录复用系统交互");
            revision = vm.DraftRevision;
            InvokeMenu(view, "FontsMenuButton", i => Equals(i.Header, "清空"));
            check(vm.FontsDirectory == "" && vm.OutputDirectory == output && !vm.CanStart && vm.DraftRevision == revision + 1, "清空字体只影响对应目录与提交条件");
            InvokeMenu(view, "OutputMenuButton", i => Equals(i.Header, "清空"));
            check(vm.OutputDirectory == "" && !FindMenuItem(view, "OutputMenuButton", i => Equals(i.Header, "打开目录")).IsEnabled, "清空输出目录即时禁用打开入口");
            check(new[] { "FontsMenuButton", "OutputMenuButton" }.All(n => ((MenuFlyout)view.FindControl<Button>(n)!.Flyout!).Items.OfType<MenuItem>().Select(i => i.Header).SequenceEqual(new object[] { "打开目录", "清空" })), "目录菜单只有打开和清空，不恢复复制入口");
            vm.VideoPath = ""; vm.FontsDirectory = fonts; vm.OutputDirectory = output; Dispatcher.UIThread.RunJobs();
            foreach (var size in new[] { (1040, 760), (1280, 900) })
            {
                window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
                Save(window, Path.Combine(root, $"workbench-{size.Item1}x{size.Item2}.png"));
                check(scroll.Extent.Width <= scroll.Viewport.Width + 0.5 && scroll.Extent.Height <= scroll.Viewport.Height + 0.5, $"默认列宽 {size.Item1}×{size.Item2} 普通外加轨道不滚动");
            }
            layout.Resize(7, 445);
            window.Close(); Until(() => !window.IsVisible);
            check(new TrackColumnLayout(new UserPreferencesStore(preferencePath)).Widths[7] == 445, "窗口关闭保存当前宽度，即使调整尚未触发结束事件");
        }
        finally { Pump(vm.CancelAndWaitAsync()); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    private sealed record CheckResult(string Name, bool Passed);
}
