using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using static UiInputTestSupport;

internal static class RealUiAcceptance
{
    public static int Run(string fixtureDirectory, string screenshotPath)
    {
        var workspace = Workspace.Discover();
        var prefix = Path.GetFullPath(Path.Combine(workspace.Root, "desktop", "artifacts")) + Path.DirectorySeparatorChar;
        var sourceFixture = Path.GetFullPath(fixtureDirectory); var requestedScreenshot = Path.GetFullPath(screenshotPath);
        if (!sourceFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !requestedScreenshot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("真实 UI 验收素材与截图必须在 desktop/artifacts/ 下。");
        var root = Path.Combine(prefix, "single-mux-round1-b", "real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<CheckResult>(); var scenarios = new List<object>(); Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null; SingleMuxViewModel? single = null;
            try
            {
                var input = Path.Combine(root, "输入 素材"); var fonts = Path.Combine(root, "字体 目录");
                Directory.CreateDirectory(input); Directory.CreateDirectory(fonts);
                var video = Path.Combine(input, "测试 视频.MKV"); var sc = Path.Combine(input, "简体 字幕.ASS"); var tc = Path.Combine(input, "繁体 字幕.ASS");
                var originals = new[] { Path.Combine(sourceFixture, "input", "phase1-source.mkv"), Path.Combine(sourceFixture, "input", "sc.ass"), Path.Combine(sourceFixture, "input", "tc.ass") };
                var originalHashes = originals.ToDictionary(path => path, Hash);
                foreach (var pair in originals.Zip(new[] { video, sc, tc })) System.IO.File.Copy(pair.First, pair.Second);
                foreach (var path in Directory.EnumerateFiles(Path.Combine(sourceFixture, "fonts-yahei"))) System.IO.File.Copy(path, Path.Combine(fonts, Path.GetFileName(path)));
                var inputs = new[] { video, sc, tc }.Concat(Directory.EnumerateFiles(fonts)).ToArray(); var beforeHashes = inputs.ToDictionary(path => path, Hash);
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var main = new MainWindowViewModel(workspace); single = main.Single;
                window = new MainWindow { DataContext = main }; window.Show(); Dispatcher.UIThread.RunJobs();
                var view = window.FindControl<SingleMuxView>("SingleView")!; var interactions = new ScriptedInteractions(); view.Interactions = interactions;
                SubtitleSlot[] assignmentSlots = [SubtitleSlot.Sc];
                interactions.AssignmentFlow = (owner, model, candidates, fixedSlot) =>
                {
                    // 使用生产分配对话框与真实确认控件，仅存储选择由替身返回隔离文件。
                    var dialog = new SubtitleAssignmentWindow(model, candidates, fixedSlot);
                    var choice = dialog.ShowDialog<IReadOnlyList<SubtitleAssignment>?>(owner);
                    Dispatcher.UIThread.Post(() =>
                    {
                        for (var i = 0; i < candidates.Count; i++) dialog.SlotSelectors[i].SelectedIndex = (int)assignmentSlots[i];
                        Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmAssignment"));
                    });
                    return choice;
                };
                Pick(window, view, interactions, "PickVideoButton", File(window, video)); Pump(single.ProbeVideoAsync(), 45);
                Check(view.FindControl<TextBlock>("VideoName")!.Text == "测试 视频.MKV" && single.SourceTracks.Any(t => t.Type == "视频" && t.Policy == "保留") && single.SourceTracks.Any(t => t.Type == "音频" && t.Policy == "保留"), "新视频选择按钮接纳真实小 fixture，摘要/源 A/V 绑定正确");
                Check(single.SourceTracks.Any(t => t.Type == "字幕" && t.Policy == "移除") && single.SourceAttachmentCount > 0 && !single.SourceTracks.Any(t => t.Type == "附件"), "真实源字幕移除和附件数量投影正确");
                Pick(window, view, interactions, "PickFontsButton", Folder(window, fonts));
                AddAss(view, interactions, File(window, sc));
                Check(single.ScSubtitle == sc && single.TcSubtitle == "" && single.ScTrack.IsDefault && interactions.AssignmentCount == 1, "真实添加轨道菜单与生产分配对话框接纳单 SC");
                RunScenario("single-ass", ["zh-Hans"]);
                assignmentSlots = [SubtitleSlot.Tc]; AddAss(view, interactions, File(window, tc));
                Check(single.ScTrack.IsDefault && !single.TcTrack.IsDefault && single.ExternalTracks.Count == 2, "新添加入口扩展 SC+TC，固定语言与默认旗标正确");
                RunScenario("sc-tc", ["zh-Hans", "zh-Hant"]);
                var scCheck = view.FindControl<ItemsControl>("ExternalRows")!.GetVisualDescendants().OfType<CheckBox>().First(c => ReferenceEquals(c.DataContext, single.ScTrack));
                Click(window, scCheck);
                Check(!single.ScTrack.IsIncluded && single.ScSubtitle == sc && !single.TcTrack.IsDefault, "新行加入开关暂时禁用 SC，保留路径与 TC 显式旗标");
                RunScenario("tc-only", ["zh-Hant"]);
                var published = single.OutputPath; var publishedHash = Hash(published); var previousResult = single.Result;
                Check(single.PrimaryActionText == "再次封装" && view.FindControl<Button>("OpenOutputButton")!.IsEffectivelyVisible, "真实成功状态提供再次封装与成品位置操作");
                Click(window, view, "StartButton"); Pump(single.RunTask, 120);
                Check(single.Result?.Status == MuxStatus.Failed && single.Result.Error?.Contains("目标已存在") == true && System.IO.File.Exists(published) && Hash(published) == publishedHash, "通过新再次封装按钮验证目标冲突仍拒绝覆盖");
                if (!single.IsResultInspectorOpen) Click(window, view, "ShowResultButton"); Save(window, Path.Combine(root, "conflict-failure-1280x900.png"));
                Check(inputs.All(path => Hash(path) == beforeHashes[path]) && originals.All(path => Hash(path) == originalHashes[path]), "三种新 UI 真实封装和冲突重试后隔离输入与原 fixture 哈希保持");

                void RunScenario(string name, string[] expectedLanguages)
                {
                    var output = Path.Combine(root, "outputs", name); Directory.CreateDirectory(output);
                    Pick(window, view, interactions, "PickOutputButton", Folder(window, output));
                    var revision = single.DraftRevision;
                    Check(single.CanStart && view.FindControl<Button>("StartButton")!.IsEffectivelyEnabled && !single.HasCurrentSuccess, name + " 新输出目录接纳后处于可提交草稿");
                    Click(window, view, "StartButton");
                    Check(single.IsRunning && !single.CanEdit && !view.FindControl<Button>("PickVideoButton")!.IsEffectivelyEnabled && view.FindControl<Button>("CancelButton")!.IsEffectivelyEnabled, name + " 真实开始控件提交并锁定编辑");
                    Save(window, Path.Combine(root, name + "-running-1280x900.png"));
                    main.ShowEnvironmentCommand.Execute(null); main.ShowSingleCommand.Execute(null); Dispatcher.UIThread.RunJobs();
                    Check(single.DraftRevision == revision && single.IsRunning, name + " 运行切页保留提交配置");
                    Pump(single.RunTask, 180); Until(() => single.StartCommand.CanExecute(null));
                    var result = single.Result ?? throw new InvalidOperationException("真实封装没有结果");
                    Check(result.Status == MuxStatus.Succeeded && System.IO.File.Exists(single.OutputPath) && single.HasCurrentSuccess && !single.IsResultHistorical, name + " Core 真实成功与当前结果回到新 UI");
                    Check(result.Checks.Count > 0 && System.IO.File.Exists(single.LogPath), name + " 成品 QC 和日志可定位");
                    var probeTask = new SingleMuxService(workspace).ProbeAsync(single.OutputPath); Pump(probeTask, 45); var probe = probeTask.Result;
                    var subtitles = probe.Tracks.Where(t => t.Type == "subtitles").ToArray();
                    var expectedTracks = new[] { single.ScTrack, single.TcTrack }.Where(t => t.IsIncluded && !string.IsNullOrWhiteSpace(t.InputPath)).ToArray();
                    Check(subtitles.Length == expectedLanguages.Length && expectedLanguages.All(language => subtitles.Count(t => t.LanguageIetf == language) == 1), name + " 实际产物只含本次启用的字幕语言");
                    Check(expectedTracks.All(t => subtitles.Any(actual => actual.LanguageIetf == t.Language && actual.Name == t.Name && actual.IsDefault == t.IsDefault && actual.IsForced == t.IsForced)), name + " 实际字幕名称/default/forced 与新 UI 配置相同");
                    Check(probe.Tracks.Count(t => t.Type == "video") == single.SourceTracks.Count(t => t.Type == "视频") && probe.Tracks.Count(t => t.Type == "audio") == single.SourceTracks.Count(t => t.Type == "音频") && probe.Attachments.Count > 0 && probe.Attachments.All(a => !a.FileName.Equals("LegacyFont.ttf", StringComparison.OrdinalIgnoreCase)), name + " 产物保留源 A/V、移除旧附件并包含新子集字体");
                    Pump(single.OutputProbeTask, 45);
                    Check(single.HasOutputCounts && single.OutputMediaCounts.Contains("视频") && single.OutputAttachmentSummary.StartsWith(probe.Attachments.Count.ToString(), StringComparison.Ordinal), name + " 结果详情成品概览来自真实成品只读探测");
                    var line = System.IO.File.ReadAllLines(single.LogPath).Single(l => l.Contains("请求 {", StringComparison.Ordinal));
                    var requestJson = line[(line.IndexOf("请求 ", StringComparison.Ordinal) + 3)..]; var submitted = JsonSerializer.Deserialize<MuxRequest>(requestJson)!;
                    Check(submitted.Subtitles.Count == expectedLanguages.Length && submitted.Subtitles.All(s => expectedLanguages.Contains(s.Language)), name + " Core 原始日志证明新控件提交启用快照");
                    System.IO.File.WriteAllText(Path.Combine(root, name + "-request.json"), requestJson);
                    System.IO.File.Copy(single.LogPath, Path.Combine(root, name + "-task.log"));
                    System.IO.File.WriteAllText(Path.Combine(root, name + "-probe.json"), JsonSerializer.Serialize(probe, new JsonSerializerOptions { WriteIndented = true }));
                    if (!single.IsResultInspectorOpen) Click(window, view, "ShowResultButton");
                    foreach (var size in new[] { (1280, 900), (1040, 760) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
                        Save(window, Path.Combine(root, $"{name}-success-{size.Item1}x{size.Item2}.png"));
                    }
                    window.Width = 1280; window.Height = 900; Dispatcher.UIThread.RunJobs();
                    if (name == "sc-tc") { Directory.CreateDirectory(Path.GetDirectoryName(requestedScreenshot)!); Save(window, requestedScreenshot); }
                    scenarios.Add(new { Name = name, Input = submitted, Result = result, OutputProbe = probe, OutputSha256 = Hash(single.OutputPath), NewUiEntry = "PickVideoButton / PickFontsButton / AddTrackButton 菜单(字幕 ASS) + SubtitleAssignmentWindow / 行加入 CheckBox / PickOutputButton / StartButton 键盘事件", NativePicker = "由公开 StorageProvider 获取真实本地项目，选择器返回被替身代替；非原生选择器证明" });
                    Console.WriteLine($"Output [{name}]: {single.OutputPath}");
                }
            }
            catch (Exception ex) { failure = ex; Console.Error.WriteLine(ex); if (single is not null) Console.Error.WriteLine(single.ResultSummary); }
            finally
            {
                if (window is not null)
                {
                    try { var closed = false; window.Closed += (_, _) => closed = true; window.Close(); Until(() => closed, 45); Check(true, "新 UI 三种真实任务后安全关闭等待完成"); }
                    catch (Exception ex) { failure ??= ex; }
                }
            }
            void Check(bool condition, string name) { checks.Add(new(name, condition)); Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name); if (!condition) throw new InvalidOperationException(name); }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        System.IO.File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Scope = "当前新 MainWindow + 真实 Core/AFS/MKVToolNix 小 fixture，headless 控件事件；不证明原生选择器/Explorer/DPI/播放器", Checks = checks, Scenarios = scenarios, Failure = failure?.ToString(), ArtifactRoot = root }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Real new UI: {checks.Count(c => c.Passed)}/{checks.Count} PASS; Report: {Path.Combine(root, "report.json")}");
        return failure is null ? 0 : 1;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));
    private sealed record CheckResult(string Name, bool Passed);
}
