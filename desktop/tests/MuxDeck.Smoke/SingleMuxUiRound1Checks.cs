using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using static UiInputTestSupport;

internal static class SingleMuxUiRound1Checks
{
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "single-mux-round1-b", "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<CheckResult>(); Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                void Check(bool condition, string name) { checks.Add(new(name, condition)); Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name); if (!condition) throw new InvalidOperationException(name); }
                Verify(root, Check);
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        System.IO.File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Scope = "三图稿新布局、模拟 DragDrop、真实控件键盘事件、原生交互替身、受控 Core；不证明 Explorer/原生选择器/DPI/播放器", Checks = checks, Failure = failure?.ToString(), ArtifactRoot = root }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"UI round1: {checks.Count(c => c.Passed)}/{checks.Count} PASS; Report: {Path.Combine(root, "report.json")}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Verify(string root, Action<bool, string> check)
    {
        var fixture = Path.Combine(root, "中文 空格"); var fonts = Path.Combine(fixture, "字体目录"); var output = Path.Combine(root, "output");
        Directory.CreateDirectory(fonts); Directory.CreateDirectory(output);
        var video = Path.Combine(fixture, "很长的中文 视频文件名用于检查省略与完整路径访问.MKV"); var video2 = Path.Combine(fixture, "替换视频.MP4");
        var sc = Path.Combine(fixture, "SC.ASS"); var tc = Path.Combine(fixture, "TC.ass"); var other = Path.Combine(fixture, "替换.ASS");
        var tc2 = Path.Combine(fixture, "TC2.ass"); var other2 = Path.Combine(fixture, "替换2.ASS");
        foreach (var path in new[] { video, video2 }) System.IO.File.WriteAllText(path, "controlled video");
        foreach (var path in new[] { sc, tc, other, tc2, other2 }) System.IO.File.WriteAllText(path, Ass);
        var workspace = Path.Combine(root, "workspace"); PrepareTools(workspace);
        var runner = new UiRunner(); var main = new MainWindowViewModel(new Workspace(workspace), new SingleMuxService(new Workspace(workspace), runner));
        var vm = main.Single; var window = new MainWindow { DataContext = main }; window.Show(); Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<SingleMuxView>("SingleView")!; var interactions = new ScriptedInteractions(); view.Interactions = interactions;
        var videoZone = view.FindControl<Border>("VideoDropZone")!; var assZone = view.FindControl<Border>("AssDropZone")!; var fontZone = view.FindControl<Border>("FontsDropZone")!;
        var trackInspector = view.FindControl<Border>("TrackInspector")!; var resultInspector = view.FindControl<Border>("ResultInspector")!;
        string Draft() => JsonSerializer.Serialize(new { vm.VideoPath, vm.ScSubtitle, vm.TcSubtitle, vm.FontsDirectory, vm.OutputDirectory, vm.OutputFileName, vm.DraftRevision, vm.ScTrack.Name, ScDefault = vm.ScTrack.IsDefault, ScForced = vm.ScTrack.IsForced, ScIncluded = vm.ScTrack.IsIncluded, TcIncluded = vm.TcTrack.IsIncluded });
        Button RowSelect(TrackRowViewModel row) => view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("row-select") && ReferenceEquals(b.DataContext, row));
        CheckBox RowCheck(TrackRowViewModel row) => view.GetVisualDescendants().OfType<CheckBox>().First(c => ReferenceEquals(c.DataContext, row));
        void Snapshot(string name, bool normal = false)
        {
            foreach (var size in new[] { (1280, 900), (1040, 760) })
            {
                window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
                if (normal && size.Item1 == 1040)
                {
                    // 普通 3+2 场景在最小窗口不得整体滚动：设置与固定状态同时可见，轨道清单不出现滚动。
                    var settings = fontZone.Bounds; var settingsPoint = fontZone.TranslatePoint(default, window)!.Value;
                    var statusPoint = view.FindControl<Border>("StatusPanel")!.TranslatePoint(default, window)!.Value;
                    var scroll = view.FindControl<ScrollViewer>("TrackListScroll")!;
                    check(settingsPoint.Y + settings.Height <= statusPoint.Y + 0.5 && settings.Height >= 117.5,
                        $"{name} 1040×760 设置面板完整可见且未被状态栏覆盖（settingsBottom={settingsPoint.Y + settings.Height:0.#}, statusTop={statusPoint.Y:0.#}, height={settings.Height:0.#}）");
                    check(scroll.Extent.Height <= scroll.Viewport.Height + 0.5,
                        $"{name} 1040×760 普通轨道清单无需滚动（extent={scroll.Extent.Height:0.#}, viewport={scroll.Viewport.Height:0.#}）");
                }
                Save(window, Path.Combine(root, $"{name}-{size.Item1}x{size.Item2}.png"));
                var panel = view.FindControl<Border>("StatusPanel")!;
                var start = view.FindControl<Button>("StartButton")!; var cancel = view.FindControl<Button>("CancelButton")!;
                var primary = vm.IsRunning ? cancel : start;
                var bounds = primary.Bounds; var point = primary.TranslatePoint(new Point(0, 0), window)!.Value;
                var panelPoint = panel.TranslatePoint(new Point(0, 0), window)!.Value;
                check(bounds.Width > 0 && point.Y >= panelPoint.Y && point.Y + bounds.Height <= window.Bounds.Height && point.X + bounds.Width <= window.Bounds.Width,
                    $"{name} {size.Item1}×{size.Item2} 固定主操作位于窗口内");
            }
            window.Width = 1280; window.Height = 900; Dispatcher.UIThread.RunJobs();
        }
        try
        {
            check(view.FindControl<TabControl>("MuxTabs")!.Items.Count == 3 && !view.FindControl<TabItem>("DeveloperLogTab")!.IsVisible && view.FindControl<TextBlock>("CurrentState")!.Text == "请选择源视频", "Release 封装/字体页可见，日志默认隐藏，空态绑定真实");
            check(!trackInspector.IsVisible && !resultInspector.IsVisible && Math.Abs(trackInspector.Width - 320) < 0.5 && Math.Abs(resultInspector.Height - 300) < 0.5, "两个 Overlay 初始隐藏且为 320 宽 / 300 高");
            view.ShowDeveloperLog = true; Dispatcher.UIThread.RunJobs(); check(view.FindControl<TabItem>("DeveloperLogTab")!.IsVisible, "明确开发开关可显示日志页"); view.ShowDeveloperLog = false;
            check(DragDrop.GetAllowDrop(videoZone) && DragDrop.GetAllowDrop(assZone) && DragDrop.GetAllowDrop(fontZone), "三个实际接收区均注册 AllowDrop");
            var addTrackMenu = ((MenuFlyout)view.FindControl<Button>("AddTrackButton")!.Flyout!).Items.OfType<MenuItem>().ToArray();
            check(addTrackMenu.Length == 2 && addTrackMenu[0].Header!.ToString() == "字幕（ASS）" && addTrackMenu[0].IsEnabled && addTrackMenu[1].Header!.ToString() == "音频（尚未支持）" && !addTrackMenu[1].IsEnabled, "添加轨道菜单：字幕(ASS) 可用、音频(尚未支持) 禁用");
            check(!DragDrop.GetAllowDrop(trackInspector) && !DragDrop.GetAllowDrop(resultInspector), "Overlay 本身不是接收区，拖入不会穿透到底下轨道清单");
            Snapshot("empty");
            var videoItem = File(window, video); var video2Item = File(window, video2); var scItem = File(window, sc); var tcItem = File(window, tc); var otherItem = File(window, other);
            using var videoData = Files(videoItem);
            var revision = vm.DraftRevision;
            var over = Drag(videoZone, DragDrop.DragOverEvent, videoData);
            var videoHint = view.FindControl<TextBlock>("VideoDropHint")!;
            check(over.DragEffects == DragDropEffects.Copy && videoZone.Classes.Contains("accepting") && videoHint.IsVisible && videoHint.Text!.Contains("释放") && vm.DraftRevision == revision, "有效视频悬停有动作说明和高亮，草稿未写入");
            Drag(videoZone, DragDrop.DragLeaveEvent, videoData);
            check(!videoZone.Classes.Contains("accepting") && !videoHint.IsVisible, "离开清理高亮并隐藏提示");
            Drag(videoZone, DragDrop.DropEvent, videoData); Pump(view.InputTask); Pump(vm.ProbeVideoAsync());
            check(vm.VideoPath == video && vm.SourceTracks.Count == 3 && vm.SourceAttachmentCount == 3 && vm.DisplayDuration == "01:02:03" && view.FindControl<TextBlock>("VideoName")!.Text == Path.GetFileName(video), "模拟视频 Drop 走新接纳入口，摘要含可靠时分秒");
            check(vm.SourceTracks.Any(t => t.Type == "视频") && vm.SourceTracks.Any(t => t.Type == "音频") && vm.SourceTracks.Any(t => t.Type == "字幕") && vm.SourceGroupTitle.Contains("3 条"), "源分组标题使用真实轨道数量");
            Snapshot("long-path");
            using var mixed = Files(videoItem, scItem);
            var before = Draft();
            var rejected = Drag(videoZone, DragDrop.DragOverEvent, mixed); Drag(videoZone, DragDrop.DropEvent, mixed); Pump(view.InputTask);
            check(rejected.DragEffects == DragDropEffects.None && !videoZone.Classes.Contains("accepting") && before == Draft() && vm.InteractionMessage.Contains("单个"), "混合数据整组拒绝，无接纳高亮，原草稿保持");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(video));
            Drag(videoZone, DragDrop.DropEvent, text); Pump(view.InputTask);
            check(Draft() == before && vm.InteractionMessage.Contains("文本"), "纯文本绝对路径不能升级为文件数据");
            using var url = new DataTransfer(); url.Add(DataTransferItem.CreateText("https://example.test/video.mkv"));
            Drag(videoZone, DragDrop.DropEvent, url); Pump(view.InputTask);
            check(Draft() == before && vm.InteractionMessage.Contains("URL"), "URL 拖入拒绝");
            using var wrongAss = Files(videoItem); Drag(assZone, DragDrop.DropEvent, wrongAss); Pump(view.InputTask);
            check(Draft() == before && vm.InteractionMessage.Contains("ASS"), "视频拖到字幕区整体拒绝");
            interactions.Confirm = false; Pick(window, view, interactions, "PickVideoButton", video2Item);
            check(Draft() == before && interactions.ConfirmCount == 1, "实际视频按钮选择后取消替换不改状态");
            interactions.Confirm = true; Pick(window, view, interactions, "PickVideoButton", video2Item); Pump(vm.ProbeVideoAsync());
            check(vm.VideoPath == video2 && interactions.ConfirmCount == 2 && !videoZone.Classes.Contains("accepting"), "实际视频按钮替换与 Drop 共用 Prepare/Commit");
            var pickRevision = vm.DraftRevision; Pick(window, view, interactions, "PickVideoButton");
            check(vm.DraftRevision == pickRevision && vm.VideoPath == video2, "选择器取消不改原输入");
            using var fontsData = Files(Folder(window, fonts)); Drag(fontZone, DragDrop.DropEvent, fontsData); Pump(view.InputTask);
            check(vm.FontsDirectory == fonts && vm.InteractionMessage.Contains("封装时检查"), "字体目录 Drop 仅设置目录，未预检");
            Pick(window, view, interactions, "PickOutputButton", Folder(window, output));
            check(vm.OutputDirectory == output, "输出目录只通过真实选择按钮接纳");
            interactions.CancelAssignment = true;
            using var assData = Files(scItem, tcItem); var assBefore = Draft(); Drag(assZone, DragDrop.DropEvent, assData); Pump(view.InputTask);
            check(Draft() == assBefore && interactions.AssignmentCount == 1, "两 ASS 模拟 Drop 取消分配整体保持");
            interactions.CancelAssignment = false; Drag(assZone, DragDrop.DropEvent, assData); Pump(view.InputTask);
            check(vm.ScSubtitle == sc && vm.TcSubtitle == tc && vm.ScTrack.IsDefault && !vm.TcTrack.IsDefault && !assZone.Classes.Contains("accepting"), "双 ASS 经显式 SC/TC 分配一次提交");
            using var duplicate = Files(scItem, scItem); before = Draft(); AddAss(view, interactions, scItem, scItem); interactions.NextPick = [];
            check(Draft() == before && vm.InteractionMessage.Contains("重复"), "重复 ASS 经添加轨道菜单不部分写入");
            interactions.NextPick = [scItem, tcItem, otherItem]; InvokeMenu(view, "AddTrackButton", i => i.Header?.ToString()!.Contains("ASS") == true); Pump(view.InputTask);
            check(Draft() == before && vm.InteractionMessage.Contains("1–2"), "ASS 超量整体拒绝");
            var disappears = Path.Combine(fixture, "已删除.ass"); System.IO.File.WriteAllText(disappears, Ass); var disappearsItem = File(window, disappears); System.IO.File.Delete(disappears);
            interactions.NextPick = [scItem, disappearsItem]; InvokeMenu(view, "AddTrackButton", i => i.Header?.ToString()!.Contains("ASS") == true); Pump(view.InputTask);
            check(Draft() == before && vm.InteractionMessage.Contains("不存在"), "无效第二候选整体保持");
            var sourceRows = view.FindControl<ItemsControl>("SourceRows")!;
            check(sourceRows.ItemCount == 3 && !vm.SourceTracks.Any(t => t.Type == "附件"), "源分组正确投影，不铺开附件行");
            var videoRow = vm.SourceTracks.First(t => t.Type == "视频"); var audioRow = vm.SourceTracks.First(t => t.Type == "音频"); var sourceSubRow = vm.SourceTracks.First(t => t.Type == "字幕");
            check(RowCheck(videoRow).IsChecked == true && !RowCheck(videoRow).IsEffectivelyEnabled && RowCheck(audioRow).IsChecked == true && !RowCheck(audioRow).IsEffectivelyEnabled, "源视频/音频包含状态勾选且只读");
            check(RowCheck(sourceSubRow).IsChecked == false && !RowCheck(sourceSubRow).IsEffectivelyEnabled, "源字幕包含状态未勾选且只读");
            var scCheck = RowCheck(vm.ScTrack); var tcCheck = RowCheck(vm.TcTrack);
            check(scCheck.IsChecked == true && scCheck.IsEffectivelyEnabled && tcCheck.IsEffectivelyEnabled, "外加字幕包含状态可切换");
            Click(window, tcCheck);
            check(!vm.TcTrack.IsIncluded && vm.TcTrack.InputPath == tc, "复选框键盘操作禁用字幕，不误触所选行");
            Click(window, tcCheck); check(vm.TcTrack.IsIncluded, "复选框恢复启用并保留路径");
            var scButton = RowSelect(vm.ScTrack); var tcButton = RowSelect(vm.TcTrack);
            var scPeerName = ControlAutomationPeer.CreatePeerForElement(scButton)?.GetName() ?? "";
            var tcPeerName = ControlAutomationPeer.CreatePeerForElement(tcButton)?.GetName() ?? "";
            var sourcePeerName = ControlAutomationPeer.CreatePeerForElement(RowSelect(sourceSubRow))?.GetName() ?? "";
            check(scPeerName.Contains("SC") && scPeerName.Contains("查看") && !scPeerName.StartsWith("Avalonia.", StringComparison.Ordinal), $"SC 行控件真实 peer 名称辨认动作与轨道（实际：{scPeerName}）");
            check(tcPeerName.Contains("TC") && !tcPeerName.StartsWith("Avalonia.", StringComparison.Ordinal) && scPeerName != tcPeerName, $"TC 行控件真实 peer 名称与 SC 可区分（实际：{tcPeerName}）");
            check(sourcePeerName.Length > 0 && !sourcePeerName.StartsWith("Avalonia.", StringComparison.Ordinal), $"源行控件 peer 名称不是控件类型文本（实际：{sourcePeerName}）");
            var scroll = view.FindControl<ScrollViewer>("TrackListScroll")!; var scrollBefore = scroll.Offset;
            Click(window, scButton);
            check(trackInspector.IsVisible && !resultInspector.IsVisible && ReferenceEquals(vm.SelectedTrack, vm.ScTrack) && scroll.Offset == scrollBefore, "点击行主要区域打开右侧轨道属性，不改动清单滚动位置");
            check(scButton.IsFocused || view.FindControl<Border>("TrackInspector")!.IsFocused, "面板打开后焦点移入 Inspector 或保持触发入口");
            var trackNameInput = view.FindControl<TextBox>("TrackNameInput")!;
            check(trackNameInput.IsEffectivelyEnabled && view.FindControl<Border>("AssDropZone")!.IsHitTestVisible && !videoZone.IsHitTestVisible && !fontZone.IsHitTestVisible, "外加轨道属性可编辑，被遮视频/设置区退出命中测试，轨道清单保持可用");
            trackNameInput.Text = "自定义简体轨";
            Click(window, view, "ForcedFlagInput"); Dispatcher.UIThread.RunJobs();
            check(vm.ScTrack.Name == "自定义简体轨" && vm.ScTrack.IsForced, "Inspector 的真实控件回写名称和 forced");
            var sourceButton = RowSelect(sourceSubRow); Click(window, sourceButton);
            check(ReferenceEquals(vm.SelectedTrack, sourceSubRow) && !trackNameInput.IsEffectivelyEnabled && !view.FindControl<StackPanel>("ExternalActions")!.IsVisible, "源轨属性按真实能力只读，外加操作不显示");
            Click(window, RowSelect(vm.ScTrack));
            var replaceButton = view.FindControl<Button>("ReplaceExternalButton")!;
            interactions.NextPick = [otherItem]; Click(window, replaceButton); Pump(view.InputTask);
            check(vm.ScSubtitle == other && vm.ScTrack.Name == "自定义简体轨" && ReferenceEquals(vm.SelectedTrack, vm.ScTrack), $"Inspector 更换文件明确槽位并保留元数据（slot={vm.ScSubtitle}, name={vm.ScTrack.Name}）");
            var removeButton = view.FindControl<Button>("RemoveExternalButton")!;
            Click(window, removeButton); Pump(view.InputTask);
            check(vm.ScSubtitle == "", "Inspector 移除轨道清空对应槽位");
            interactions.NextPick = [scItem]; InvokeMenu(view, "AddTrackButton", i => i.Header?.ToString()!.Contains("ASS") == true); Pump(view.InputTask);
            // 普通 3 源轨 + SC/TC：先记录与三图对应的普通、轨道属性、多轨状态（此时尚无结果，状态为“输入已齐全”）。
            view.FindControl<ScrollViewer>("TrackListScroll")!.Offset = default; vm.CloseInspectors(); Dispatcher.UIThread.RunJobs();
            Snapshot("normal", normal: true);
            Click(window, RowSelect(vm.ScTrack));
            Snapshot("track-inspector");
            vm.CloseInspectors(); Dispatcher.UIThread.RunJobs();
            var videoYBefore = videoZone.TranslatePoint(default, window)!.Value.Y; var statusYBefore = view.FindControl<Border>("StatusPanel")!.TranslatePoint(default, window)!.Value.Y;
            runner.SourceTrackCount = 8; Pump(vm.ProbeVideoAsync());
            var multiScroll = view.FindControl<ScrollViewer>("TrackListScroll")!;
            check(multiScroll.Extent.Height > multiScroll.Viewport.Height + 0.5, $"多轨只在清单内部滚动（extent={multiScroll.Extent.Height:0.#}, viewport={multiScroll.Viewport.Height:0.#}）");
            check(videoZone.TranslatePoint(default, window)!.Value.Y == videoYBefore && view.FindControl<Border>("StatusPanel")!.TranslatePoint(default, window)!.Value.Y == statusYBefore && fontZone.IsVisible, "多轨仍不重排摘要/设置/状态");
            Snapshot("multi-track");
            runner.SourceTrackCount = 3; Pump(vm.ProbeVideoAsync());
            // ---- F1/F2：目录派生状态与 Inspector 路径刷新（真实按钮/菜单/绑定文本） ----
            bool Within(Visual? child, Visual ancestor) { for (Visual? v = child; v is not null; v = v.GetVisualParent()) if (ReferenceEquals(v, ancestor)) return true; return false; }
            Button ByAutomation(string name) => view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
            // 菜单项在弹出层里才继承 DataContext，按真实路径打开后再读 IsEnabled。
            bool MenuEnabled(string buttonName)
            {
                var button = view.FindControl<Button>(buttonName)!;
                var flyout = (MenuFlyout)button.Flyout!;
                flyout.ShowAt(button); Dispatcher.UIThread.RunJobs();
                var enabled = flyout.Items.OfType<MenuItem>().First().IsEnabled;
                flyout.Hide(); Dispatcher.UIThread.RunJobs();
                return enabled;
            }
            var fontOpen = ByAutomation("打开字体目录"); var outputOpen = ByAutomation("打开输出目录");
            var outputName = view.FindControl<TextBox>("OutputNameInput")!; var fm = TopLevel.GetTopLevel(view)!.FocusManager!;
            vm.FontsDirectory = ""; vm.OutputDirectory = ""; Dispatcher.UIThread.RunJobs();
            check(!vm.HasFontsDirectory && !vm.HasOutputDirectory && !fontOpen.IsEffectivelyEnabled && !outputOpen.IsEffectivelyEnabled, "清空字体/输出目录后派生状态与打开入口立即禁用");
            check(!MenuEnabled("FontsMoreButton") && !MenuEnabled("OutputMoreButton"), "清空目录后次级菜单复制入口禁用");
            vm.FontsDirectory = fonts; Dispatcher.UIThread.RunJobs();
            check(vm.HasFontsDirectory && fontOpen.IsEffectivelyEnabled && MenuEnabled("FontsMoreButton") && !vm.HasOutputDirectory && !outputOpen.IsEffectivelyEnabled,
                "设置字体目录后其打开/菜单入口启用，输出目录仍禁用");
            vm.OutputDirectory = output; Dispatcher.UIThread.RunJobs();
            check(vm.HasOutputDirectory && outputOpen.IsEffectivelyEnabled && MenuEnabled("OutputMoreButton"), "设置输出目录后其打开/菜单入口启用");
            vm.FontsDirectory = ""; Dispatcher.UIThread.RunJobs();
            check(!vm.HasFontsDirectory && !fontOpen.IsEffectivelyEnabled, "再次清空字体目录同步禁用");
            vm.FontsDirectory = fonts; Dispatcher.UIThread.RunJobs();
            Click(window, RowSelect(vm.ScTrack)); Dispatcher.UIThread.RunJobs();
            var inspectorPath = view.FindControl<TextBlock>("InspectorPathText")!;
            var revisionBeforeReplace = vm.DraftRevision;
            var scReplace = vm.PrepareSubtitleInputs([new SubtitleAssignment(SubtitleSlot.Sc, new InputCandidate(other, InputCandidateKind.LocalFile))]);
            check(scReplace.Succeeded && scReplace.Plan!.RequiresReplacementConfirmation, "同槽替换 SC 经 Prepare 明示确认需求");
            vm.CommitInput(scReplace.Plan!, true); Dispatcher.UIThread.RunJobs();
            check(vm.ScSubtitle == other && ReferenceEquals(vm.SelectedTrack, vm.ScTrack) && inspectorPath.Text == other && vm.SelectedTrackFilePath == other && vm.ScTrack.InputPath == other,
                "同槽替换后 Inspector 路径文本与真实 InputPath/提交快照一致");
            check(vm.DraftRevision == revisionBeforeReplace + 1 && vm.ScTrack.Name == "自定义简体轨" && vm.ScTrack.IsForced, "同槽替换只增加一次草稿修订并保留名称与显式旗标");
            var pathBeforeTc = inspectorPath.Text;
            var tcReplace = vm.PrepareSubtitleInputs([new SubtitleAssignment(SubtitleSlot.Tc, new InputCandidate(tc2, InputCandidateKind.LocalFile))]);
            vm.CommitInput(tcReplace.Plan!, true); Dispatcher.UIThread.RunJobs();
            check(vm.TcSubtitle == tc2 && inspectorPath.Text == pathBeforeTc && ReferenceEquals(vm.SelectedTrack, vm.ScTrack), "未选槽位替换不改所选 SC 的 Inspector 路径");
            vm.RemoveSubtitle(SubtitleSlot.Sc); Dispatcher.UIThread.RunJobs();
            check(inspectorPath.Text == vm.SelectedTrackFilePath, "移除后 Inspector 路径与新的所选轨道保持一致");
            interactions.NextPick = [scItem]; InvokeMenu(view, "AddTrackButton", i => i.Header?.ToString()!.Contains("ASS") == true); Pump(view.InputTask);
            // ---- F3：面板打开后真实 Tab/Shift+Tab 避让被遮区域 ----
            Click(window, RowSelect(vm.ScTrack)); Dispatcher.UIThread.RunJobs();
            check(trackInspector.IsVisible && Within(fm.GetFocusedElement() as Visual, trackInspector), "打开轨道属性后焦点落在可用面板入口");
            var nameBeforeHiddenEdit = vm.OutputFileName;
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, ""); Dispatcher.UIThread.RunJobs();
            var afterShiftTab = fm.GetFocusedElement() as Control;
            check(afterShiftTab is not null && !ReferenceEquals(afterShiftTab, outputName) && !Within(afterShiftTab, fontZone),
                $"轨道面板打开时 Shift+Tab 不进入被遮设置区（实际：{afterShiftTab?.Name}）");
            window.KeyTextInput("hidden-edit"); Dispatcher.UIThread.RunJobs();
            check(vm.OutputFileName == nameBeforeHiddenEdit, "被遮控件不接受键盘文本输入");
            vm.ToggleResultInspector(); Dispatcher.UIThread.RunJobs();
            view.FindControl<Border>("ResultInspector")!.Focus(); Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, ""); Dispatcher.UIThread.RunJobs();
            var resultShiftTab = fm.GetFocusedElement() as Control;
            check(resultShiftTab is not null && !ReferenceEquals(resultShiftTab, outputName) && !Within(resultShiftTab, fontZone),
                $"结果面板打开时 Shift+Tab 也不进入被遮设置区（实际：{resultShiftTab?.Name}）");
            vm.ToggleResultInspector(); Dispatcher.UIThread.RunJobs();
            // ---- F4：关闭恢复当前视觉树中的行/入口，不沿用已脱离控件 ----
            Click(window, RowSelect(vm.ScTrack)); Dispatcher.UIThread.RunJobs();
            var scRebuild = vm.PrepareSubtitleInputs([new SubtitleAssignment(SubtitleSlot.Sc, new InputCandidate(other2, InputCandidateKind.LocalFile))]);
            vm.CommitInput(scRebuild.Plan!, true); Dispatcher.UIThread.RunJobs();
            Click(window, view, "TrackInspectorCloseButton"); Dispatcher.UIThread.RunJobs();
            var restored = fm.GetFocusedElement() as Control;
            check(!trackInspector.IsVisible && restored is not null && TopLevel.GetTopLevel(restored) == window && restored.IsEffectivelyVisible && ReferenceEquals(restored, RowSelect(vm.ScTrack)),
                "同槽替换重建行后关闭，焦点回到当前视觉树中的 SC 行");
            Click(window, RowSelect(vm.TcTrack)); Dispatcher.UIThread.RunJobs();
            vm.RemoveSubtitle(SubtitleSlot.Tc); Dispatcher.UIThread.RunJobs();
            Click(window, view, "TrackInspectorCloseButton"); Dispatcher.UIThread.RunJobs();
            var restoredAfterRemove = fm.GetFocusedElement() as Control;
            check(!trackInspector.IsVisible && restoredAfterRemove is not null && TopLevel.GetTopLevel(restoredAfterRemove) == window && restoredAfterRemove.IsEffectivelyVisible,
                $"移除所选轨道后关闭仍有有效可见焦点（实际：{restoredAfterRemove?.Name}）");
            interactions.Slots = [SubtitleSlot.Tc]; interactions.NextPick = [tcItem];
            InvokeMenu(view, "AddTrackButton", i => i.Header?.ToString()!.Contains("ASS") == true); Pump(view.InputTask);
            interactions.Slots = null;
            // ---- F5：轨道清单方向键导航与 Enter 打开 ----
            Click(window, RowSelect(vm.ScTrack)); Dispatcher.UIThread.RunJobs();
            var revisionBeforeNav = vm.DraftRevision; var scIncluded = vm.ScTrack.IsIncluded; var tcIncluded = vm.TcTrack.IsIncluded;
            RowSelect(vm.ScTrack).Focus(); Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
            check(ReferenceEquals(fm.GetFocusedElement(), RowSelect(vm.TcTrack)) && ReferenceEquals(vm.SelectedTrack, vm.TcTrack), "清单获焦时下方向键从 SC 到 TC 并原位更新面板");
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, ""); Dispatcher.UIThread.RunJobs();
            check(ReferenceEquals(fm.GetFocusedElement(), RowSelect(vm.ScTrack)) && ReferenceEquals(vm.SelectedTrack, vm.ScTrack), "上方向键从 TC 回到 SC");
            check(vm.DraftRevision == revisionBeforeNav && vm.ScTrack.IsIncluded == scIncluded && vm.TcTrack.IsIncluded == tcIncluded, "方向导航不改变草稿修订或包含状态");
            vm.CloseInspectors(); Dispatcher.UIThread.RunJobs();
            RowSelect(vm.ScTrack).Focus(); Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Dispatcher.UIThread.RunJobs();
            check(trackInspector.IsVisible && ReferenceEquals(vm.SelectedTrack, vm.ScTrack), "Enter 打开对应轨道属性");
            vm.CloseInspectors(); Dispatcher.UIThread.RunJobs();
            outputName.Focus(); Dispatcher.UIThread.RunJobs();
            var nameBeforeArrow = vm.OutputFileName;
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
            check(ReferenceEquals(fm.GetFocusedElement(), outputName) && vm.OutputFileName == nameBeforeArrow, "清单外方向键不被行导航逻辑截获");
            // 运行/取消/失败路径。
            Click(window, RowSelect(vm.TcTrack));
            check(trackInspector.IsVisible, "切换轨道原位打开右侧面板");
            view.FindControl<ScrollViewer>("TrackListScroll")!.Focus();
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a"); Dispatcher.UIThread.RunJobs();
            check(trackInspector.IsVisible, "非 Esc 输入不关闭面板，点击空白不自动关闭");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
            check(!trackInspector.IsVisible, "Esc 关闭当前 Overlay");
            var pendingPick = new TaskCompletionSource<IReadOnlyList<IStorageItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
            interactions.PickOverride = () => pendingPick.Task; Click(window, view, "PickVideoButton");
            var pendingTask = view.InputTask; var pickCount = interactions.PickCount;
            Drag(assZone, DragDrop.DropEvent, assData);
            check(ReferenceEquals(pendingTask, view.InputTask) && interactions.PickCount == pickCount && vm.InteractionMessage.Contains("正在处理"), "未完成选择期间防止重叠拖入流程");
            Click(window, view, "StartButton"); Until(() => runner.AfsStarted.Task.IsCompleted);
            check(vm.IsRunning && !view.FindControl<Button>("PickVideoButton")!.IsEffectivelyEnabled && !RowCheck(vm.TcTrack).IsEffectivelyEnabled && !trackNameInput.IsEffectivelyEnabled, "真实开始按钮提交后路径/加入/Inspector 控件锁定");
            var runningDraft = Draft(); pendingPick.TrySetResult([videoItem]); Pump(pendingTask); interactions.PickOverride = null;
            check(Draft() == runningDraft && vm.InteractionMessage.Contains("已锁定"), "选择器迟到返回不能更改运行任务");
            Snapshot("running");
            Click(window, view, "CancelButton"); Until(() => runner.CleanupStarted.Task.IsCompleted);
            check(vm.IsCancelling && !view.FindControl<Button>("CancelButton")!.IsEffectivelyEnabled && !vm.CanEdit, "取消清理期间禁用重复取消和输入");
            Snapshot("cancelling"); runner.CleanupRelease.TrySetResult(true); Pump(vm.RunTask);
            check(vm.Result?.Status == MuxStatus.Cancelled && vm.CanEdit, "取消完成后恢复编辑，保持 Core 取消结果");
            runner.BlockAfs = false; Click(window, view, "StartButton"); Pump(vm.RunTask);
            check(vm.Result?.Status == MuxStatus.Failed && view.FindControl<TextBlock>("CurrentState")!.Text == "封装失败，请查看结果详情", "失败主区短摘要");
            Click(window, view, "ShowResultButton");
            check(resultInspector.IsVisible && view.FindControl<TextBox>("RawError")!.Text == vm.Result!.Error && view.FindControl<ItemsControl>("CheckRows")!.ItemsSource is not null, "结果详情保存原始 Error 并逐项列出真实 Checks");
            Snapshot("failure");
            var copyButton = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "复制诊断")); Click(window, copyButton);
            check(interactions.CopiedText?.Contains(vm.Result!.Error!) == true && interactions.CopiedText.Contains("Checks"), "诊断复制保留原始错误与结构化结果");
            interactions.FailCopy = true; Click(window, copyButton); check(vm.InteractionMessage.Contains("复制失败"), "剪贴板失败可见且保留结果"); interactions.FailCopy = false;
            Click(window, view, "OpenLogButton"); check(interactions.OpenedPath == vm.LogPath, "日志定位传递实际完整任务路径");
            interactions.FailOpen = true; Click(window, view, "OpenLogButton"); check(vm.InteractionMessage.Contains("打开失败"), "平台打开失败可见"); interactions.FailOpen = false;
            Click(window, RowSelect(vm.ScTrack));
            check(trackInspector.IsVisible && !resultInspector.IsVisible && !videoZone.IsHitTestVisible && !fontZone.IsHitTestVisible, "打开轨道属性时结果面板关闭，被遮摘要/设置区退出命中测试");
            Click(window, view, "ShowResultButton");
            check(resultInspector.IsVisible && !trackInspector.IsVisible && videoZone.IsHitTestVisible && !fontZone.IsHitTestVisible, "打开结果详情时轨道属性关闭，仅设置区退出命中测试");
            Click(window, view, "ShowResultButton");
            check(!resultInspector.IsVisible && fontZone.IsHitTestVisible, "结果详情按钮可再次关闭并恢复被遮区域命中测试");
            var revisionBeforePresentation = vm.DraftRevision; view.FindControl<TabControl>("MuxTabs")!.SelectedIndex = 1; main.ShowEnvironmentCommand.Execute(null); main.ShowSingleCommand.Execute(null); view.FindControl<TabControl>("MuxTabs")!.SelectedIndex = 0;
            check(vm.DraftRevision == revisionBeforePresentation && !vm.IsResultHistorical, "新页签/导航不改变结果修订");
            // 完整原始按键遍历：每次只从正常入口打开面板，不强制逐站 Focus。
            void SendKey(Key key, bool shift = false)
            {
                var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
                var physical = key == Avalonia.Input.Key.Tab ? PhysicalKey.Tab : PhysicalKey.Space;
                window.KeyPress(key, modifiers, physical, ""); window.KeyRelease(key, modifiers, physical, ""); Dispatcher.UIThread.RunJobs();
            }
            List<Control> WalkTabs(bool backward)
            {
                var seen = new List<Control>();
                for (var i = 0; i < 256; i++)
                {
                    SendKey(Avalonia.Input.Key.Tab, backward);
                    if (fm.GetFocusedElement() is not Control focused) throw new InvalidOperationException("Tab 没有有效焦点");
                    if (seen.Contains(focused)) return seen;
                    seen.Add(focused);
                }
                throw new InvalidOperationException("Tab 遍历未形成稳定循环");
            }
            var sidebar = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav")).ToArray();
            var environmentButton = sidebar.Single(b => Equals(b.Content, "环境检测"));
            var singleButton = sidebar.Single(b => Equals(b.Content, "单个封装"));
            var listScroll = view.FindControl<ScrollViewer>("TrackListScroll")!;
            window.Width = 1040; window.Height = 760; listScroll.Offset = default; Dispatcher.UIThread.RunJobs();
            void VerifyTraversal(bool resultPanel, bool backward, string scene)
            {
                if (vm.HasOpenInspector) Click(window, view, vm.IsTrackInspectorOpen ? "TrackInspectorCloseButton" : "ResultInspectorCloseButton");
                Click(window, resultPanel ? view.FindControl<Button>("ShowResultButton")! : RowSelect(vm.ScTrack));
                var stops = WalkTabs(backward);
                var direction = backward ? "反向" : "正向";
                check(sidebar.All(stops.Contains), $"{scene} {direction} Tab 可到达全部可见侧栏导航");
                check(stops.Contains(view.FindControl<Button>("StartButton")!) && stops.Contains(view.FindControl<Button>("ShowResultButton")!), $"{scene} {direction} Tab 保留固定状态操作");
                check(!stops.Any(c => Within(c, fontZone)) && (resultPanel || !stops.Any(c => Within(c, videoZone))), $"{scene} {direction} Tab 排除被遮复合设置/摘要");
                if (resultPanel)
                {
                    check(stops.Contains(view.FindControl<Button>("ResultInspectorCloseButton")!) && stops.Contains(ByAutomation("复制成品路径")) && stops.Contains(copyButton) && stops.Contains(view.FindControl<Button>("OpenLogButton")!), $"{scene} {direction} Tab 可到达结果关闭/复制/日志");
                    var panelTop = resultInspector.TranslatePoint(default, window)!.Value.Y;
                    var viewportTop = listScroll.TranslatePoint(default, window)!.Value.Y;
                    var viewportBottom = viewportTop + listScroll.Bounds.Height;
                    check(stops.Where(c => c is Button b && b.Classes.Contains("row-select")).All(c =>
                    {
                        var y = c.TranslatePoint(default, window)!.Value.Y;
                        return y < Math.Min(panelTop, viewportBottom) && y + c.Bounds.Height > viewportTop;
                    }), $"{scene} {direction} Tab 只到清单可见且未被结果面板完全盖住的行");
                }
                else
                {
                    check(!stops.Contains(view.FindControl<Button>("AddTrackButton")!) && !stops.Contains(view.FindControl<Button>("ShowAttachmentsButton")!), $"{scene} {direction} Tab 不到被轨道面板盖住的添加/附件入口");
                    check(stops.Contains(RowSelect(vm.ScTrack)) && stops.Contains(RowCheck(vm.ScTrack)) && stops.Contains(view.FindControl<Button>("TrackInspectorCloseButton")!), $"{scene} {direction} Tab 保留可见轨道/包含和面板操作");
                }
            }
            VerifyTraversal(false, false, "轨道面板"); VerifyTraversal(false, true, "轨道面板");
            VerifyTraversal(true, false, "结果面板"); VerifyTraversal(true, true, "结果面板");
            // 从面板实际 Tab 到侧栏后激活导航，隐藏封装页时使用原窗口导航，返回保留草稿/结果。
            var navigationDraft = Draft(); var navigationResult = vm.Result;
            for (var i = 0; !ReferenceEquals(fm.GetFocusedElement(), environmentButton) && i < 128; i++) SendKey(Avalonia.Input.Key.Tab);
            SendKey(Avalonia.Input.Key.Space);
            check(main.IsEnvironment && Draft() == navigationDraft && ReferenceEquals(vm.Result, navigationResult), "面板打开时键盘侧栏可切环境页且保留草稿/结果");
            for (var i = 0; !ReferenceEquals(fm.GetFocusedElement(), singleButton) && i < 128; i++) SendKey(Avalonia.Input.Key.Tab, true);
            SendKey(Avalonia.Input.Key.Space);
            check(main.IsSingle && resultInspector.IsVisible && Draft() == navigationDraft, "键盘返回封装页保留非模态面板与草稿");
            vm.CloseInspectors(); runner.SourceTrackCount = 8; Pump(vm.ProbeVideoAsync());
            listScroll.Offset = default; Dispatcher.UIThread.RunJobs();
            Click(window, RowSelect(vm.SourceTracks[0])); RowSelect(vm.SourceTracks[0]).Focus();
            for (var i = 0; i < vm.SourceTracks.Count + 1; i++)
            {
                window.KeyPress(Avalonia.Input.Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                window.KeyRelease(Avalonia.Input.Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
            }
            check(ReferenceEquals(fm.GetFocusedElement(), RowSelect(vm.TcTrack)) && ReferenceEquals(vm.SelectedTrack, vm.TcTrack) && listScroll.Offset.Y > 0, "多轨轨道面板 Down 跨视口滚到 TC 并跟随属性");
            for (var i = 0; i < vm.SourceTracks.Count + 1; i++)
            {
                window.KeyPress(Avalonia.Input.Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
                window.KeyRelease(Avalonia.Input.Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, ""); Dispatcher.UIThread.RunJobs();
            }
            check(ReferenceEquals(fm.GetFocusedElement(), RowSelect(vm.SourceTracks[0])) && vm.SelectedTrack?.Id == "0", "多轨轨道面板 Up 跨视口返回第一条源轨");
            Click(window, view, "ShowResultButton"); RowSelect(vm.SourceTracks[0]).Focus();
            for (var i = 0; i < vm.SourceTracks.Count + 1; i++)
            {
                window.KeyPress(Avalonia.Input.Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                window.KeyRelease(Avalonia.Input.Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
            }
            var tcPoint = RowSelect(vm.TcTrack).TranslatePoint(default, window)!.Value;
            check(ReferenceEquals(fm.GetFocusedElement(), RowSelect(vm.TcTrack)) && tcPoint.Y < resultInspector.TranslatePoint(default, window)!.Value.Y && tcPoint.Y + RowSelect(vm.TcTrack).Bounds.Height > listScroll.TranslatePoint(default, window)!.Value.Y, "多轨结果面板方向键滚入目标，焦点留在面板之外的可见清单");
            listScroll.Offset = new Vector(0, listScroll.Extent.Height); Dispatcher.UIThread.RunJobs();
            VerifyTraversal(true, false, "多轨滚动结果面板"); VerifyTraversal(true, true, "多轨滚动结果面板");
            vm.CloseInspectors(); runner.SourceTrackCount = 3; Pump(vm.ProbeVideoAsync()); listScroll.Offset = default; Dispatcher.UIThread.RunJobs();
            // 源对象会重建，未选字幕变更不能借引用失效而跳选外加槽位。
            foreach (var sourceId in new[] { "0", "1" })
            {
                var selectedSource = vm.SourceTracks.Single(t => t.Id == sourceId);
                Click(window, RowSelect(selectedSource));
                var sourceRevision = vm.DraftRevision;
                var replacement = vm.TcSubtitle == tc ? tc2 : tc;
                var prepared = vm.PrepareSubtitleInputs([new(SubtitleSlot.Tc, new(replacement, InputCandidateKind.LocalFile))]);
                var committed = vm.CommitInput(prepared.Plan!, true); Dispatcher.UIThread.RunJobs();
                check(committed.Succeeded && ReferenceEquals(vm.SelectedTrack, vm.SourceTracks.Single(t => t.Id == sourceId)) && inspectorPath.Text == vm.VideoPath && vm.DraftRevision == sourceRevision + 1, $"选中源轨 {sourceId} 后替换未选 TC 保持当前源对象/路径，修订只增加一次");
                var sourceBeforeReject = vm.SelectedTrack;
                var sourceRejected = vm.PrepareSubtitleInputs([new(SubtitleSlot.Sc, new(vm.ScSubtitle == sc ? other2 : sc, InputCandidateKind.LocalFile))]);
                check(!vm.CommitInput(sourceRejected.Plan!, false).Succeeded && ReferenceEquals(vm.SelectedTrack, sourceBeforeReject) && vm.DraftRevision == sourceRevision + 1, $"源轨 {sourceId} 未确认的字幕替换保持选择和修订");
                var scCommitted = vm.CommitInput(sourceRejected.Plan!, true); Dispatcher.UIThread.RunJobs();
                check(scCommitted.Succeeded && ReferenceEquals(vm.SelectedTrack, vm.SourceTracks.Single(t => t.Id == sourceId)) && inspectorPath.Text == vm.VideoPath && vm.DraftRevision == sourceRevision + 2, $"源轨 {sourceId} 确认替换未选 SC 仍保持源选择与路径");
                vm.RemoveSubtitle(SubtitleSlot.Tc); Dispatcher.UIThread.RunJobs();
                check(vm.SelectedTrack?.Id == sourceId && vm.SelectedTrack.IsEditable == false && inspectorPath.Text == vm.VideoPath, $"源轨 {sourceId} 下移除未选 TC 不改变源选择");
                vm.TcSubtitle = tc; Dispatcher.UIThread.RunJobs();
            }
            var oldSourceSelection = vm.SelectedTrack;
            vm.VideoPath = vm.VideoPath == video ? video2 : video;
            check(vm.SelectedTrack is null, "更换源视频立即清空旧源选择，不继承相同数字轨道 Id");
            Pump(vm.ProbeVideoAsync());
            check(!ReferenceEquals(vm.SelectedTrack, oldSourceSelection), "新源探测不会恢复旧视频轨道对象");
            vm.CloseInspectors(); window.Width = 1280; window.Height = 900; Dispatcher.UIThread.RunJobs();
            WarningPolicyChecks.VerifyUiWarningPresentation(root, check, warningModel =>
            {
                view.DataContext = warningModel; Dispatcher.UIThread.RunJobs();
                if (!warningModel.IsResultInspectorOpen) warningModel.ToggleResultInspector();
                Dispatcher.UIThread.RunJobs();
                check(view.FindControl<ItemsControl>("WarningRows")!.ItemCount == 3 && warningModel.State == SingleMuxState.SucceededWithWarnings && !view.FindControl<TabItem>("DeveloperLogTab")!.IsVisible && view.FindControl<Button>("OpenOutputButton")!.IsEffectivelyVisible,
                    "Release 成功附提示真实结果显示三条 message 和文件位置操作，日志仍隐藏");
                Snapshot("success-with-warnings");
                var warningsBefore = warningModel.Result;
                Click(window, view, "OpenOutputButton"); check(interactions.OpenedPath == warningModel.OutputPath, "成功文件位置操作传递实际成品路径");
                System.IO.File.Delete(warningModel.OutputPath); Click(window, view, "OpenOutputButton"); check(warningModel.InteractionMessage.Contains("打开失败") && ReferenceEquals(warningsBefore, warningModel.Result), "成品消失后打开失败可见且不覆盖成功结果");
                Pump(warningModel.CancelAndWaitAsync()); view.DataContext = vm;
            });
        }
        finally { runner.CleanupRelease.TrySetResult(true); Pump(vm.CancelAndWaitAsync()); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    private static void PrepareTools(string root)
    {
        foreach (var relative in new[] { "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe", "assfontsubset/AssFontSubset.Console.exe", "python/Scripts/pyftsubset.exe", "python/Scripts/ttx.exe" })
        { var path = Path.Combine(root, "bin", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); System.IO.File.WriteAllText(path, "controlled runner"); }
    }
    private sealed record CheckResult(string Name, bool Passed);
    private const string Ass = """
        [Script Info]
        ScriptType: v4.00+
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,1,1,0,2,10,10,10,1
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,English
        """;
    private sealed class UiRunner : IMuxToolRunner
    {
        public bool FailProbe { get; set; }
        public bool BlockAfs { get; set; } = true;
        public int SourceTrackCount { get; set; } = 3;
        public int AttachmentCount { get; set; } = 3;
        public TaskCompletionSource<bool> AfsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
        {
            if (request.Arguments.Contains("-J"))
                return FailProbe ? new(7, "", "受控同路径探测失败") : new(0, JsonSerializer.Serialize(new
                {
                    container = new { properties = new { duration = 3_723_000_000_000L } },
                    tracks = Enumerable.Range(0, SourceTrackCount).Select(i => new { id = i, type = i == 0 ? "video" : i == SourceTrackCount - 1 ? "subtitles" : "audio", codec = i == 0 ? "AV1" : i == SourceTrackCount - 1 ? "ASS" : "AAC", properties = new { codec_id = "fixture", language = "jpn", track_name = "源轨道 " + i, pixel_dimensions = i == 0 ? "1920x1080" : null } }).ToArray(),
                    attachments = Enumerable.Range(1, AttachmentCount).Select(i => new { id = i, file_name = "旧源附件 " + i + ".ttf", content_type = "font/ttf", size = 12 }).ToArray()
                }), "");
            if (request.Arguments.Contains("--subset-backend"))
            {
                AfsStarted.TrySetResult(true);
                if (BlockAfs)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { CleanupStarted.TrySetResult(true); await CleanupRelease.Task; throw; }
                }
                return new(7, "仅供诊断的输出", "受控子集化失败");
            }
            return new(0, "", "");
        }
    }
}
