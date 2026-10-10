using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;

internal static class SingleMuxRound1Checks
{
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "single-mux-round1-a", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<CheckResult>();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
                void Check(bool condition, string name)
                {
                    checks.Add(new(name, condition));
                    Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
                    if (!condition) throw new InvalidOperationException(name);
                }
                Verify(root, Check);
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new
        {
            Scope = "任务 A 状态与输入接纳；受控 IMuxToolRunner，不证明真实工具封装/原生拖入/DPI",
            ArtifactRoot = root, Checks = checks, Failure = failure?.ToString()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{checks.Count(c => c.Passed)}/{checks.Count} PASS\nReport: {Path.Combine(root, "report.json")}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }

    private static void Verify(string root, Action<bool, string> check)
    {
        var input = Path.Combine(root, "中文 素材");
        var fonts = Path.Combine(input, "字体 目录");
        var output = Path.Combine(root, "独立 输出");
        Directory.CreateDirectory(fonts); Directory.CreateDirectory(output);
        var video = Path.Combine(input, "源 视频.MKV");
        var secondVideo = Path.Combine(input, "新 视频.MP4");
        var sc = Path.Combine(input, "简体 字幕.ASS");
        var tc = Path.Combine(input, "繁体 字幕.ass");
        var replacement = Path.Combine(input, "替换 字幕.ASS");
        var unsupported = Path.Combine(input, "unsupported.mov");
        foreach (var path in new[] { video, secondVideo, unsupported }) File.WriteAllText(path, "controlled fixture");
        foreach (var path in new[] { sc, tc, replacement }) File.WriteAllText(path, Ass);
        var runner = new ControlledRunner();
        var workspace = Path.Combine(root, "workspace");
        PrepareTools(workspace);
        var vm = new SingleMuxViewModel(new SingleMuxService(new Workspace(workspace), runner));
        InputCandidate FileCandidate(string path) => new(path, InputCandidateKind.LocalFile);
        SubtitleAssignment Assignment(SubtitleSlot slot, string path) => new(slot, FileCandidate(path));
        InputAcceptanceResult Commit(InputAcceptanceResult prepared, bool confirm = false)
        {
            if (!prepared.Succeeded || prepared.Plan is null) throw new InvalidOperationException(prepared.Message);
            return vm.CommitInput(prepared.Plan, confirm);
        }
        string Configuration() => JsonSerializer.Serialize(new { vm.VideoPath, vm.ScSubtitle, vm.TcSubtitle, vm.FontsDirectory, vm.OutputDirectory, vm.OutputFileName, vm.DraftRevision,
            Sc = new { vm.ScTrack.Name, vm.ScTrack.IsDefault, vm.ScTrack.IsForced, vm.ScTrack.IsIncluded }, Tc = new { vm.TcTrack.Name, vm.TcTrack.IsDefault, vm.TcTrack.IsForced, vm.TcTrack.IsIncluded } });
        void RejectUnchanged(Func<InputAcceptanceResult> action, InputAcceptanceError expected, string name)
        {
            var before = Configuration(); var result = action();
            check(result.Error == expected && Configuration() == before && vm.InteractionMessage == result.Message, name);
        }
        try
        {
            check(vm.State == SingleMuxState.NoVideo && !vm.CanStart && vm.Result is null, "空态和开始条件真实");
            var preparedVideo = vm.PrepareVideoInput([FileCandidate(video)]);
            check(preparedVideo.Succeeded && !preparedVideo.Plan!.RequiresReplacementConfirmation && vm.VideoPath == "" && vm.DraftRevision == 0, "准备候选不写草稿；取消只需丢弃计划");
            check(Commit(preparedVideo).Succeeded && vm.VideoFileName == "源 视频.MKV" && vm.VideoFullPath == video && vm.ResolvedOutputFileName == "源 视频.muxed.mkv", "中文空格大写 MKV 接纳与默认输出名");
            Pump(vm.ProbeVideoAsync());
            check(vm.ProbeState == VideoProbeState.Succeeded && vm.VideoDimensions == "1920x1080" && vm.VideoCodec == "AV1" && vm.VideoDurationNanoseconds == 2_500_000_000 && vm.MediaSummary.Contains("2.5 秒"), "摘要只使用探测的尺寸编码时长");
            check(vm.SourceTrackCount == 3 && vm.SourceTracks.Count == 3 && vm.SourceAttachmentCount == 2 && vm.SourceAttachments.Count == 2 && vm.Tracks.Count == 5 && vm.SourceTracks.All(t => vm.Tracks.Contains(t)), "新源分组与旧列表一致，附件独立供详情使用");
            check(vm.SourceTracks.Where(t => t.Type is "视频" or "音频").All(t => t.Policy == "保留") && vm.SourceTracks.Single(t => t.Type == "字幕").Policy == "移除" && !vm.SourceTracks.Any(t => t.CanEdit), "源 A/V 保留与源字幕移除为只读策略");
            check(Commit(vm.PrepareFontsDirectory([new(fonts, InputCandidateKind.LocalDirectory)])).Succeeded && vm.InteractionMessage.Contains("封装时检查"), "字体目录仅接纳，消息不声称检查通过");
            check(Commit(vm.PrepareOutputDirectory([new(output, InputCandidateKind.LocalDirectory)])).Succeeded, "独立输出目录选择可复用接纳入口");
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])).Succeeded && vm.ScTrack.IsDefault && !vm.TcTrack.IsDefault && vm.ScTrack.InputPath == sc, "初次 SC 使用既有默认规则");
            check(vm.RemoveSubtitle(SubtitleSlot.Sc).Succeeded && vm.ScSubtitle == "" && !vm.ExternalTracks.Contains(vm.ScTrack), "按槽位移除清空路径和展示槽位");
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Tc, tc)])).Succeeded && vm.TcTrack.IsDefault, "TC-only 使用既有默认规则");
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])).Succeeded && vm.ScTrack.IsDefault && !vm.TcTrack.IsDefault && vm.ExternalTracks.Count == 2, "SC+TC 使用既有默认规则和外加分组");
            vm.ScTrack.Name = "显式 简体"; vm.ScTrack.IsDefault = false; vm.ScTrack.IsForced = true;
            vm.TcTrack.Name = "显式 繁体"; vm.TcTrack.IsDefault = true; vm.TcTrack.IsForced = true;
            var replacePlan = vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, replacement)]);
            check(replacePlan.Succeeded && replacePlan.Plan!.RequiresReplacementConfirmation, "同槽替换明示确认需求");
            RejectUnchanged(() => vm.CommitInput(replacePlan.Plan!), InputAcceptanceError.ConfirmationRequired, "未确认替换不改路径、元数据和修订");
            check(Commit(replacePlan, true).Succeeded && vm.ScTrack.Name == "显式 简体" && !vm.ScTrack.IsDefault && vm.ScTrack.IsForced, "确认同槽替换保留名称与显式旗标");
            var beforeNoop = Configuration();
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, replacement.ToUpperInvariant())])).Succeeded && Configuration() == beforeNoop, "Windows 大小写同路径同槽接纳无害");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Tc, replacement.ToUpperInvariant())]), InputAcceptanceError.Duplicate, "与既有另一槽位重复整体拒绝");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, sc.ToUpperInvariant())]), InputAcceptanceError.Duplicate, "同批同路径分两槽整体拒绝");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, Path.Combine(input, "缺失.ass"))]), InputAcceptanceError.MissingPath, "第二候选缺失时整组拒绝，首候选不写入");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, video)]), InputAcceptanceError.WrongType, "第二候选错误类型时整组拒绝");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Sc, tc)]), InputAcceptanceError.InvalidSlot, "同槽重复分配拒绝");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment((SubtitleSlot)42, sc)]), InputAcceptanceError.InvalidSlot, "未知槽位拒绝");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, tc), Assignment(SubtitleSlot.Sc, replacement)]), InputAcceptanceError.InvalidCount, "ASS 超量整体拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate(video), FileCandidate(secondVideo)]), InputAcceptanceError.InvalidCount, "两个视频整体拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate(video), FileCandidate(sc)]), InputAcceptanceError.InvalidCount, "视频 ASS 混合操作整体拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate(unsupported)]), InputAcceptanceError.WrongType, "视频 MOV 不在本轮支持范围");
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate(Path.Combine(input, "不存在.MKV"))]), InputAcceptanceError.MissingPath, "不存在的视频拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([new("https://example.test/video.mkv", InputCandidateKind.NonLocal)]), InputAcceptanceError.NonLocal, "URL 数据拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([new(video, InputCandidateKind.NonLocal)]), InputAcceptanceError.NonLocal, "纯文本绝对路径数据拒绝");
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate("relative.mkv")]), InputAcceptanceError.InvalidPath, "相对路径拒绝");
            RejectUnchanged(() => vm.PrepareFontsDirectory([FileCandidate(sc)]), InputAcceptanceError.WrongType, "字体区文件类型拒绝");
            RejectUnchanged(() => vm.PrepareFontsDirectory([new(fonts, InputCandidateKind.LocalDirectory), new(output, InputCandidateKind.LocalDirectory)]), InputAcceptanceError.InvalidCount, "多个字体文件夹整体拒绝");
            RejectUnchanged(() => vm.PrepareFontsDirectory([new(Path.Combine(input, "不存在目录"), InputCandidateKind.LocalDirectory)]), InputAcceptanceError.MissingPath, "缺失字体文件夹拒绝");
            var swapRevision = vm.DraftRevision;
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, tc), Assignment(SubtitleSlot.Tc, replacement)]), true).Succeeded && vm.ScSubtitle == tc && vm.TcSubtitle == replacement && vm.DraftRevision == swapRevision + 1, "整组交换旧槽位合法且一次增加修订");
            check(vm.ScTrack.Name == "显式 简体" && !vm.ScTrack.IsDefault && vm.ScTrack.IsForced && vm.TcTrack.IsDefault && vm.TcTrack.IsForced, "整组替换不重设显式旗标");
            var stale = vm.PrepareVideoInput([FileCandidate(secondVideo)]).Plan!;
            vm.OutputFileName = "changed.mkv";
            RejectUnchanged(() => vm.CommitInput(stale, true), InputAcceptanceError.Stale, "弹窗期间草稿变化拒绝迟到计划");
            var disappearing = Path.Combine(input, "消失.ass"); File.WriteAllText(disappearing, Ass);
            var disappearingPlan = vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, disappearing)]).Plan!;
            File.Delete(disappearing);
            RejectUnchanged(() => vm.CommitInput(disappearingPlan, true), InputAcceptanceError.MissingPath, "提交时重新检查候选存在性");
            var videoRevision = vm.DraftRevision;
            vm.SelectedTrack = vm.SourceTracks[0];
            var subtitlesBeforeVideo = (vm.ScSubtitle, vm.TcSubtitle, vm.ScTrack.Name, vm.ScTrack.IsDefault, vm.ScTrack.IsForced, vm.FontsDirectory, vm.OutputDirectory, vm.OutputFileName);
            var newVideoPlan = vm.PrepareVideoInput([FileCandidate(secondVideo)]);
            RejectUnchanged(() => vm.CommitInput(newVideoPlan.Plan!), InputAcceptanceError.ConfirmationRequired, "已有视频替换须确认");
            check(Commit(newVideoPlan, true).Succeeded && vm.SourceTracks.Count == 0 && vm.SourceAttachmentCount == 0 && vm.SelectedTrack is null && vm.DraftRevision == videoRevision + 1 && subtitlesBeforeVideo == (vm.ScSubtitle, vm.TcSubtitle, vm.ScTrack.Name, vm.ScTrack.IsDefault, vm.ScTrack.IsForced, vm.FontsDirectory, vm.OutputDirectory, vm.OutputFileName), "MP4 替换清掉旧摘要选择，保留字幕和设置");
            Pump(vm.ProbeVideoAsync());
            check(vm.VideoDimensions is null && vm.VideoCodec == "AV1" && vm.VideoDurationNanoseconds is null && !vm.MediaSummary.Contains("秒") && !vm.MediaSummary.Contains("MP4"), "未知尺寸时长省略，不按扩展名捏造容器");
            vm.OutputFileName = "";
            vm.SetSubtitleIncluded(SubtitleSlot.Sc, false);
            check(vm.ScSubtitle == tc && vm.ScTrack.Name == "显式 简体" && vm.ScTrack.IsForced && vm.ExternalTracks.Contains(vm.ScTrack) && vm.CanStart, "禁用 SC 保留路径属性和详情行");
            check(Commit(vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)]), true).Succeeded && !vm.ScTrack.IsIncluded && vm.ScTrack.Name == "显式 简体" && !vm.ScTrack.IsDefault && vm.ScTrack.IsForced, "禁用槽位替换保留禁用状态与显式元数据");
            var notifiedDisabled = false;
            vm.StartCommand.CanExecuteChanged += (_, _) => { if (!vm.StartCommand.CanExecute(null)) notifiedDisabled = true; };
            vm.SetSubtitleIncluded(SubtitleSlot.Tc, false);
            var startsBefore = runner.AfsCalls;
            Pump(vm.StartAsync()); vm.StartCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            check(!vm.CanStart && !vm.StartCommand.CanExecute(null) && !vm.IsRunning && runner.AfsCalls == startsBefore && notifiedDisabled, "全禁用时 CanStart、命令与直接 Start 均不可执行，并通知控件");
            vm.SetSubtitleIncluded(SubtitleSlot.Tc, true);
            check(vm.TcTrack.Name == "显式 繁体" && vm.TcTrack.IsDefault && vm.TcTrack.IsForced, "启用恢复不丢名称或旗标");
            var lockedPlan = vm.PrepareVideoInput([FileCandidate(video)]).Plan!;
            _ = vm.StartAsync(); Until(() => runner.AfsStarted.Task.IsCompleted);
            check(vm.State == SingleMuxState.Running && !vm.CanEdit && !vm.CanEditSelectedTrack && !vm.TcTrack.CanEdit && vm.ProgressStage == MuxStage.Subsetting && vm.IsIndeterminate && vm.ProgressMessage.Contains("子集化"), "运行状态、真实阶段消息和不定进度");
            var runningConfiguration = Configuration();
            vm.TcTrack.Name = "运行中篡改"; vm.TcTrack.IsDefault = false; vm.TcTrack.IsForced = false; vm.TcTrack.IsIncluded = false;
            vm.VideoPath = video; vm.FontsDirectory = output; vm.OutputDirectory = fonts; vm.OutputFileName = "运行中.mkv";
            check(Configuration() == runningConfiguration, "运行中路径、元数据和启用绑定均被模型锁定");
            RejectUnchanged(() => vm.CommitInput(lockedPlan, true), InputAcceptanceError.Locked, "选择器返回后的提交重新核验编辑锁");
            RejectUnchanged(() => vm.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)]), InputAcceptanceError.Locked, "运行中拒绝准备接纳");
            RejectUnchanged(() => vm.RemoveSubtitle(SubtitleSlot.Tc), InputAcceptanceError.Locked, "运行中移除拒绝");
            vm.CancelCommand.Execute(null); Until(() => runner.CleanupStarted.Task.IsCompleted);
            check(vm.IsRunning && vm.IsCancelling && vm.State == SingleMuxState.Cancelling && !vm.CanEdit && !vm.RunTask.IsCompleted, "取消清理完成前继续锁定输入");
            RejectUnchanged(() => vm.CommitInput(lockedPlan, true), InputAcceptanceError.Locked, "取消清理期间拒绝迟到提交");
            runner.CleanupRelease.TrySetResult(true); Pump(vm.RunTask);
            var snapshot = ReadRequest(vm, root, "tc-only-request.json");
            check(snapshot.Subtitles.Count == 1 && snapshot.Subtitles[0] == new SubtitleInput(replacement, "显式 繁体", "zh-Hant", true, true), "实际 Core 请求快照只提交启用的 TC ASS 和显式旗标");
            check(vm.Result?.Status == MuxStatus.Cancelled && vm.State == SingleMuxState.Cancelled && vm.CanEdit && !vm.IsResultHistorical && File.Exists(vm.LogPath), "取消完成恢复编辑，保留当前结果和日志");
            var cancelledResult = vm.Result;
            RejectUnchanged(() => vm.PrepareVideoInput([FileCandidate(unsupported)]), InputAcceptanceError.WrongType, "结果后接纳失败仍不改草稿");
            check(ReferenceEquals(vm.Result, cancelledResult) && !vm.IsResultHistorical, "交互错误不覆盖 Core 结果");
            var resultRevision = vm.DraftRevision;
            vm.SelectedTrack = vm.TcTrack;
            vm.VideoPath = vm.VideoPath; vm.TcTrack.Name = vm.TcTrack.Name; vm.TcTrack.IsDefault = vm.TcTrack.IsDefault;
            check(vm.DraftRevision == resultRevision && !vm.IsResultHistorical, "选中行与同值赋值不使当前结果失效");
            vm.SetSubtitleIncluded(SubtitleSlot.Sc, true);
            check(vm.IsResultHistorical && vm.ResultLabel == "上次结果" && vm.StatusText.Contains("草稿已修改") && ReferenceEquals(vm.Result, cancelledResult), "启用变化将结果标记为历史且保留诊断");

            // 每次重新提交得到当前失败结果，再逐类修改，避免只验证第一次变更。
            runner.BlockAfs = false;
            var changes = new (string Name, Action Change)[]
            {
                ("视频", () => vm.VideoPath = video),
                ("字幕路径", () => vm.ScSubtitle = tc),
                ("字体目录", () => vm.FontsDirectory = input),
                ("输出目录", () => vm.OutputDirectory = root),
                ("输出文件名", () => vm.OutputFileName = "新成品.mkv"),
                ("轨道名称", () => vm.ScTrack.Name = "新名称"),
                ("默认旗标", () => vm.ScTrack.IsDefault = !vm.ScTrack.IsDefault),
                ("强制旗标", () => vm.ScTrack.IsForced = !vm.ScTrack.IsForced)
            };
            foreach (var change in changes)
            {
                Pump(vm.StartAsync());
                var result = vm.Result;
                check(result?.Status == MuxStatus.Failed && vm.State == SingleMuxState.Failed && !vm.IsResultHistorical && vm.StateSummary == "封装失败，请查看结果详情" && vm.ResultSummary.Contains(result.Error!), change.Name + "变更前新任务结果当前且保留原始错误");
                var revision = vm.DraftRevision;
                change.Change();
                check(vm.DraftRevision == revision + 1 && vm.IsResultHistorical && ReferenceEquals(vm.Result, result) && File.Exists(vm.LogPath), change.Name + "变化标记上次结果，保留诊断日志");
            }
            Pump(vm.StartAsync());
            var metadataRevision = vm.DraftRevision;
            for (var i = 0; i < 5; i++) vm.ScSubtitle = i % 2 == 0 ? replacement : sc;
            var beforeMetadata = vm.DraftRevision;
            vm.ScTrack.Name = "一次修改";
            check(vm.DraftRevision == beforeMetadata + 1 && beforeMetadata == metadataRevision + 5, "重建列表不累积元数据事件订阅");
            vm.ScTrack.IsDefault = true; vm.TcTrack.IsDefault = true;
            Pump(vm.StartAsync());
            var both = ReadRequest(vm, root, "both-default-request.json");
            check(both.Subtitles.Count == 2 && both.Subtitles.All(s => s.IsDefault), "保留既有多个默认轨提交能力");
            vm.ScTrack.IsDefault = false; vm.TcTrack.IsDefault = false;
            vm.SetSubtitleIncluded(SubtitleSlot.Tc, false); Pump(vm.StartAsync());
            var onlySc = ReadRequest(vm, root, "sc-only-request.json");
            check(onlySc.Subtitles.Count == 1 && onlySc.Subtitles[0].Language == "zh-Hans" && !onlySc.Subtitles[0].IsDefault, "禁用 TC 只提交 SC，保留零默认轨能力");
            var pastSnapshot = JsonSerializer.Serialize(onlySc);
            vm.ScTrack.Name = "任务后变更";
            check(JsonSerializer.Serialize(onlySc) == pastSnapshot, "已提交 Core 快照不受后续元数据修改影响");
            runner.BlockAfs = true;
            var previousResult = vm.Result;
            _ = vm.StartAsync();
            check(vm.IsRunning && ReferenceEquals(vm.Result, previousResult) && vm.IsResultHistorical && vm.ResultLabel == "上次结果", "再次运行期间保留上次诊断结果直至新结果返回");
            vm.CancelCommand.Execute(null); Pump(vm.RunTask);
            vm.RemoveSubtitle(SubtitleSlot.Sc);
            check(vm.ScSubtitle == "" && vm.ScTrack.InputPath == "" && !vm.CanStart, "移除最后启用字幕使槽位清空并不可启动");

            VerifyLateProbe(root, video, secondVideo, check);
            VerifyNavigation(root, sc, fonts, output, check);
            WarningPolicyChecks.VerifyUiWarningPresentation(root, check);
        }
        finally { runner.CleanupRelease.TrySetResult(true); Pump(vm.CancelAndWaitAsync()); }
    }

    private static void VerifyLateProbe(string root, string video, string secondVideo, Action<bool, string> check)
    {
        var runner = new ControlledRunner { DelayVideo = video };
        var vm = new SingleMuxViewModel(new SingleMuxService(new Workspace(Path.Combine(root, "workspace")), runner));
        vm.VideoPath = video; var oldProbe = vm.ProbeVideoAsync(); Until(() => runner.ProbeStarted.Task.IsCompleted);
        vm.VideoPath = secondVideo; Pump(vm.ProbeVideoAsync());
        check(vm.ProbeState == VideoProbeState.Succeeded && vm.VideoDimensions is null && vm.SourceTrackCount == 1, "新视频探测结果独立显示");
        runner.ProbeRelease.TrySetResult(true);
        Pump(oldProbe);
        check(vm.VideoPath == secondVideo && vm.VideoDimensions is null && vm.SourceTrackCount == 1, "忽略取消的旧探测迟到不得覆盖新视频");
        vm.VideoPath = Path.Combine(root, "缺失视频.mkv"); Pump(vm.ProbeVideoAsync());
        check(vm.ProbeState == VideoProbeState.Failed && vm.State == SingleMuxState.ProbeFailed && vm.ProbeText.Contains("不存在") && vm.SourceTrackCount == 0, "探测失败可观察且不展示旧源摘要");
        runner.ResetProbeGate(); vm.VideoPath = video; Until(() => runner.ProbeStarted.Task.IsCompleted);
        var pending = vm.PrepareVideoInput([new(secondVideo, InputCandidateKind.LocalFile)]).Plan!;
        var closing = vm.CancelAndWaitAsync();
        var revision = vm.DraftRevision;
        check(vm.IsClosing && vm.State == SingleMuxState.Closing && !closing.IsCompleted && !vm.CanEdit && !vm.CanStart && !vm.StartCommand.CanExecute(null), "关闭等待包含尚未返回的探测，并锁定命令");
        check(vm.CommitInput(pending, true).Error == InputAcceptanceError.Locked && vm.DraftRevision == revision && vm.VideoPath == video, "关闭等待拒绝接纳");
        runner.ProbeRelease.TrySetResult(true); Pump(closing);
        check(closing.IsCompletedSuccessfully && vm.SourceTrackCount == 0, "关闭等待旧探测结束，迟到结果不发布");
    }

    private static void VerifyNavigation(string root, string sc, string fonts, string output, Action<bool, string> check)
    {
        var main = new MainWindowViewModel(new Workspace(Path.Combine(root, "workspace")));
        var vm = main.Single;
        vm.VideoPath = Path.Combine(root, "不存在.mkv"); vm.ScSubtitle = sc; vm.FontsDirectory = fonts; vm.OutputDirectory = output;
        Pump(vm.ProbeVideoAsync()); Pump(vm.StartAsync());
        var revision = vm.DraftRevision; var result = vm.Result;
        main.ShowEnvironmentCommand.Execute(null); main.ShowSingleCommand.Execute(null);
        vm.SelectedTrack = vm.ScTrack;
        check(main.IsSingle && vm.DraftRevision == revision && ReferenceEquals(vm.Result, result) && !vm.IsResultHistorical, "任务结果后真实导航和选中行不改变草稿或结果标记");
        Pump(vm.CancelAndWaitAsync());
    }

    private static MuxRequest ReadRequest(SingleMuxViewModel vm, string root, string name)
    {
        // 读取 Core 自己的请求日志，证明真正提交的内容，而非复刻 ViewModel 快照构造。
        var path = vm.LogPath;
        var line = File.ReadAllLines(path).Single(l => l.Contains("请求 {", StringComparison.Ordinal));
        var json = line[(line.IndexOf("请求 ", StringComparison.Ordinal) + 3)..];
        File.WriteAllText(Path.Combine(root, name), json);
        return JsonSerializer.Deserialize<MuxRequest>(json)!;
    }
    private sealed record CheckResult(string Name, bool Passed);
    private static void PrepareTools(string root)
    {
        foreach (var relative in new[] { "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe", "assfontsubset/AssFontSubset.Console.exe", "python/Scripts/pyftsubset.exe", "python/Scripts/ttx.exe" })
        {
            var path = Path.Combine(root, "bin", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "controlled runner tool");
        }
    }
    private static void Pump(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
    private static void Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(15)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!condition()) throw new TimeoutException("受控行为未在时限内完成。");
        Dispatcher.UIThread.RunJobs();
    }
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

    private sealed class ControlledRunner : IMuxToolRunner
    {
        public bool BlockAfs { get; set; } = true;
        public int AfsCalls { get; private set; }
        public string? DelayVideo { get; set; }
        public TaskCompletionSource<bool> AfsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ProbeStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ProbeRelease { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ResetProbeGate() { ProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); ProbeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
        {
            if (request.Arguments.Contains("-J"))
            {
                var path = request.Arguments.Last();
                if (path == DelayVideo) { ProbeStarted.TrySetResult(true); await ProbeRelease.Task; }
                var detailed = Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase);
                return new(0, detailed ? DetailedProbe : UnknownProbe, "");
            }
            if (request.Arguments.Contains("--subset-backend"))
            {
                AfsCalls++; AfsStarted.TrySetResult(true);
                if (BlockAfs)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { CleanupStarted.TrySetResult(true); await CleanupRelease.Task; throw; }
                }
                return new(7, "不可从日志关键词推断状态", "受控子集化失败");
            }
            return new(0, "", "");
        }
        private const string UnknownProbe = """
            {"tracks":[{"id":0,"type":"video","codec":"AV1","properties":{"codec_id":"V_AV1","language":"und"}}],"attachments":[]}
            """;
        private const string DetailedProbe = """
            {"container":{"properties":{"duration":2500000000}},"tracks":[{"id":0,"type":"video","codec":"AV1","properties":{"codec_id":"V_AV1","language":"und","pixel_dimensions":"1920x1080"}},{"id":1,"type":"audio","codec":"AAC","properties":{"language":"jpn","audio_channels":2}},{"id":2,"type":"subtitles","codec":"ASS","properties":{"language":"chi"}}],"attachments":[{"id":1,"file_name":"旧字体.ttf","content_type":"font/ttf","size":12},{"id":2,"file_name":"cover.jpg","content_type":"image/jpeg","size":24}]}
            """;
    }
}
