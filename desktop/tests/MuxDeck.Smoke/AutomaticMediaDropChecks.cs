using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using static UiInputTestSupport;

internal static class AutomaticMediaDropChecks
{
    public static int Run(bool real = false)
    {
        var workspace = Workspace.Discover();
        var root = Path.Combine(workspace.Root, "desktop", "artifacts", "automatic-drop", (real ? "real-" : "ui-") + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var checks = new List<CheckResult>(); Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                void Check(bool result, string name) { checks.Add(new(name, result)); Console.WriteLine((result ? "PASS: " : "FAIL: ") + name); if (!result) throw new InvalidOperationException(name); }
                if (real) VerifyReal(workspace, root, Check); else Verify(root, Check);
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        System.IO.File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Scope = real ? "真实 mkvmerge/AFS/ffmpeg 链路，headless 拖放；不证明 Explorer 原生拖放" : "受控工具探测和真实控件 DragDrop；不证明 Explorer 原生拖放", Checks = checks, Failure = failure?.ToString(), ArtifactRoot = root }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Auto drop {(real ? "real" : "UI")}: {checks.Count(c => c.Passed)}/{checks.Count}; Report: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Verify(string root, Action<bool, string> check)
    {
        var files = new[] { "video.mkv", "second.mp4", "movie.zh-Hans.ass", "movie.zh-Hant.ass", "movie.eng.srt", "extra.srt", "broken.srt", "audio.jpn.flac", "two-tracks.mka", "font.ttf", "note.txt" }.ToDictionary(n => n, n => Path.Combine(root, n));
        foreach (var p in files.Values) System.IO.File.WriteAllText(p, p.EndsWith(".ass") ? Ass : Srt);
        var fonts = Path.Combine(root, "fonts"); Directory.CreateDirectory(fonts);
        var fakeWorkspace = Path.Combine(root, "workspace"); var merge = Path.Combine(fakeWorkspace, "bin", "mkvtoolnix", "mkvmerge.exe"); Directory.CreateDirectory(Path.GetDirectoryName(merge)!); System.IO.File.WriteAllText(merge, "controlled runner");
        var runner = new ImportRunner(); var vm = new MainWindowViewModel(new Workspace(fakeWorkspace), new SingleMuxService(new Workspace(fakeWorkspace), runner));
        var window = new MainWindow { DataContext = vm }; window.Show(); Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<SingleMuxView>("SingleView")!; var single = vm.Single; var interactions = new ScriptedInteractions(); view.Interactions = interactions;
        void Drop(string zone, params string[] names)
        { using var data = Files(names.Select(n => File(window, files[n])).ToArray()); Drag(view.FindControl<Border>(zone)!, DragDrop.DropEvent, data); Pump(view.InputTask); }
        string Draft() => JsonSerializer.Serialize(new { single.VideoPath, single.FontsDirectory, single.DraftRevision, Rows = single.ExternalTracks.Select(t => new { t.Id, t.InputPath, t.Name, t.Language, t.IsIncluded }) });
        try
        {
            using var mixed = Files(File(window, files["video.mkv"]), File(window, files["movie.zh-Hans.ass"]), File(window, files["movie.eng.srt"]), File(window, files["audio.jpn.flac"]));
            mixed.Add(DataTransferItem.CreateText("file:///companion-format"));
            check(SingleMuxView.ReadTransfer(mixed).Count == 4 && SingleMuxView.ReadTransfer(mixed).All(c => c.Kind == InputCandidateKind.LocalFile), "文件集合优先，伴随文本不污染本地文件列表");
            var revision = single.DraftRevision;
            var hover = Drag(view.FindControl<Border>("FontsDropZone")!, DragDrop.DragOverEvent, mixed);
            check(hover.DragEffects == DragDropEffects.Copy && single.DraftRevision == revision, "混合媒体在字体区悬停可接纳且不写草稿");
            Drag(view.FindControl<Border>("FontsDropZone")!, DragDrop.DropEvent, mixed); Pump(view.InputTask); Pump(single.ProbeVideoAsync());
            check(single.VideoPath == files["video.mkv"] && single.ExternalTracks.Count == 3 && single.DraftRevision == revision + 1, "混合视频/ASS/SRT/音频自动落位，整批只增加一次草稿版本");
            check(single.ExternalTracks.Select(t => t.DisplayId).SequenceEqual(new[] { "0", "1", "2" }) && single.ExternalTracks.All(t => t.Name == ""), "自动导入保留添加顺序数字 ID，空名称留空");
            check(single.ExternalTracks[0].Language == "zh-Hans" && single.ExternalTracks[1].Language == "en" && single.ExternalTracks[2].Language == "ja", "未指定语言从有边界的文件名标签推断，未知回退 und");
            check(single.ExternalTracks[2].IsAudio && single.ExternalTracks[2].Codec == "FLAC" && single.ExternalTracks[2].OriginTrackId == 4, "音频使用探测得到的非零 Track ID 和实际编码");
            check(single.RequiresFonts && !single.CanStart, "启用 ASS 仍要求字体目录和输出目录");
            var before = Draft(); Drop("VideoDropZone", "video.mkv", "movie.zh-Hans.ass");
            check(Draft() == before && single.InteractionMessage.Contains("已加入"), "重复已加入轨道整批拒绝，不部分更换视频");
            Drop("AssDropZone", "extra.srt", "broken.srt");
            check(Draft() == before && single.InteractionMessage.Contains("整批"), "第二个文件探测失败时首个文件也不提交");
            Drop("AssDropZone", "video.mkv", "second.mp4"); check(Draft() == before && single.InteractionMessage.Contains("一个源视频"), "两个视频整批拒绝");
            Drop("VideoDropZone", "note.txt"); check(Draft() == before && single.InteractionMessage.Contains("类型"), "未知类型拒绝且草稿保留");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(files["video.mkv"])); Drag(view.FindControl<Border>("VideoDropZone")!, DragDrop.DropEvent, text); Pump(view.InputTask);
            check(Draft() == before && single.InteractionMessage.Contains("文本"), "纯文本路径仍不冒充系统文件");
            interactions.Confirm = false; Drop("VideoDropZone", "second.mp4", "extra.srt"); check(Draft() == before, "取消源视频替换使同行字幕也不追加");
            interactions.Confirm = true; Drop("VideoDropZone", "second.mp4", "extra.srt"); check(single.VideoPath == files["second.mp4"] && single.ExternalTracks.Count == 4, "确认替换与字幕追加一次落位");
            Drop("VideoDropZone", "two-tracks.mka"); check(single.ExternalTracks.Count == 6 && single.ExternalTracks.TakeLast(2).Select(t => t.OriginTrackId).SequenceEqual(new[] { 2, 9 }), "一个音频容器自动导入多条音轨，真实 ID 不按位置猜测");
            using var fontData = Files(Folder(window, fonts)); Drag(view.FindControl<Border>("VideoDropZone")!, DragDrop.DropEvent, fontData); Pump(view.InputTask);
            check(single.FontsDirectory == fonts, "字体目录拖到视频区自动落位");
            single.OutputDirectory = Path.Combine(root, "output"); Directory.CreateDirectory(single.OutputDirectory);
            check(single.CanStart, "自动导入后的完整输入可开始");
            foreach (var row in single.ExternalTracks.Where(t => MediaFileTypes.IsAss(t.InputPath)).ToArray()) single.RemoveExternalTrack(row);
            single.FontsDirectory = "";
            check(single.CanStart && !single.RequiresFonts, "SRT/音频不要求字体目录");
            Pump(single.ProbeVideoAsync());
            var audio = single.ExternalTracks.First(t => t.IsAudio); audio.Name = "评论音轨"; audio.IsIncluded = false;
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("row-select") && ReferenceEquals(b.DataContext, audio)); Click(window, button);
            check(!view.FindControl<CheckBox>("ForcedFlagInput")!.IsEffectivelyVisible && view.FindControl<TextBlock>("InspectorPathText")!.Text == audio.InputPath, $"音频详情显示真实路径并隐藏字幕 Forced 编辑（selected={single.SelectedTrack?.Type}, forcedVisible={view.FindControl<CheckBox>("ForcedFlagInput")!.IsEffectivelyVisible}, path={view.FindControl<TextBlock>("InspectorPathText")!.Text}, expected={audio.InputPath}）");
            interactions.NextPick = [File(window, files["movie.zh-Hant.ass"])]; Click(window, view, "ReplaceExternalButton"); Pump(view.InputTask);
            check(single.ExternalTracks.Contains(audio) && audio.Name == "评论音轨" && !audio.IsIncluded, "跨类型更换拒绝，旧音轨元数据不变");
            var replacementAudio = Path.Combine(root, "replacement.flac"); System.IO.File.WriteAllText(replacementAudio, "controlled audio");
            var identity = audio.Id; var number = audio.DisplayId; audio.IsDefault = true;
            interactions.NextPick = [File(window, replacementAudio)]; Click(window, view, "ReplaceExternalButton"); Pump(view.InputTask);
            var updated = single.ExternalTracks.Single(t => t.Id == identity);
            check(updated.InputPath == replacementAudio && updated.DisplayId == number && updated.Name == "评论音轨" && !updated.IsIncluded && updated.IsDefault && updated.Language == "ja" && ReferenceEquals(single.SelectedTrack, updated), "成功更换音频保持身份、编号、名称、语言、旗标和纳入状态");
            Click(window, view, "RemoveExternalButton"); Dispatcher.UIThread.RunJobs();
            check(!single.ExternalTracks.Contains(updated) && single.ExternalTracks.Select(t => t.DisplayId).SequenceEqual(Enumerable.Range(0, single.ExternalTracks.Count).Select(i => i.ToString())), "音频详情移除与全部数字 ID 重排正确");
            single.CloseInspectors();
            using var overlayData = Files(File(window, files["movie.zh-Hant.ass"]));
            var rootDrop = new DragEventArgs(DragDrop.DropEvent, overlayData, view, new Point(10, 12), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
            view.RaiseEvent(rootDrop); Pump(view.InputTask); check(single.ExternalTracks.Any(t => t.Language == "zh-Hant"), "页签旁空白处也可自动接纳");
            var planTask = single.PrepareMediaInputAsync([new(files["font.ttf"], InputCandidateKind.LocalFile)]); Pump(planTask); var plan = planTask.Result.Plan!;
            single.OutputFileName = "changed.mkv"; before = Draft(); check(!single.CommitMediaInput(plan).Succeeded && before == Draft(), "迟到准备计划不能覆盖新草稿");
            var delayPath = Path.Combine(root, "delayed.srt"); System.IO.File.WriteAllText(delayPath, Srt); runner.Delay = true;
            var preparing = single.PrepareMediaInputAsync([new(delayPath, InputCandidateKind.LocalFile)]); Until(() => runner.Started);
            var closing = single.CancelAndWaitAsync(); Pump(closing); Pump(preparing);
            check(!preparing.Result.Succeeded && !single.CanEdit, "关闭取消正在运行的媒体识别并等待工具返回");
            Save(window, Path.Combine(root, "automatic-tracks.png"));
        }
        finally { Pump(single.CancelAndWaitAsync()); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    private static void VerifyReal(Workspace workspace, string root, Action<bool, string> check)
    {
        var tool = new ToolLocator(workspace).Inspect(); var ffmpeg = tool.Single(t => t.Name == "ffmpeg").Path ?? throw new FileNotFoundException("ffmpeg");
        var process = new ProcessRunner();
        void Run(string executable, IReadOnlyList<string> args) { var task = process.RunAsync(new ProcessRequest(executable, args)); Pump(task, 120); if (task.Result.ExitCode != 0) throw new InvalidOperationException(task.Result.StandardError); }
        var video = Path.Combine(root, "video.mp4"); var audio = Path.Combine(root, "extra.jpn.flac");
        Run(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=160x90:r=10:d=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", video]);
        Run(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2", "-c:a", "flac", audio]);
        var multiAudio = Path.Combine(root, "two.eng.mka"); var mkvVideo = Path.Combine(root, "video.mkv");
        Run(ffmpeg, ["-v", "error", "-i", audio, "-i", audio, "-map", "0:a", "-map", "1:a", "-c:a", "copy", multiAudio]);
        Run(tool.Single(t => t.Name == "mkvmerge").Path!, ["-o", mkvVideo, video]);
        var srts = new[] { "first.zh-Hans.srt", "second.zh-Hant.srt", "third.eng.srt" }.Select(n => Path.Combine(root, n)).ToArray(); foreach (var srt in srts) System.IO.File.WriteAllText(srt, Srt);
        var asses = new[] { "one.eng.ass", "two.eng.ass", "three.eng.ass" }.Select(n => Path.Combine(root, n)).ToArray(); foreach (var ass in asses) System.IO.File.WriteAllText(ass, Ass);
        var fonts = Path.Combine(root, "fonts"); Directory.CreateDirectory(fonts); System.IO.File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"), Path.Combine(fonts, "arial.ttf"));
        string[] inputs = [video, mkvVideo, audio, multiAudio, .. srts, .. asses, Path.Combine(fonts, "arial.ttf")]; var hashes = inputs.ToDictionary(p => p, p => SHA256.HashData(System.IO.File.ReadAllBytes(p)));
        foreach (var (name, added, needFonts) in new[] { ("srt-only", srts.Take(1).ToArray(), false), ("audio-only", new[] { audio }, false), ("multi-audio", new[] { multiAudio }, false), ("multi-srt", srts, false), ("multi-ass", asses, true), ("mixed", new[] { srts[0], asses[0], audio, srts[1] }, true) })
        {
            var main = new MainWindowViewModel(workspace); var vm = main.Single; var window = new MainWindow { DataContext = main }; window.Show(); Dispatcher.UIThread.RunJobs();
            var view = window.FindControl<SingleMuxView>("SingleView")!;
            try
            {
                var items = new List<IStorageItem> { File(window, name == "multi-audio" ? mkvVideo : video) }; items.AddRange(added.Select(p => File(window, p))); if (needFonts) items.Add(Folder(window, fonts));
                using var data = Files(items.ToArray()); data.Add(DataTransferItem.CreateText("伴随文本"));
                Drag(view.FindControl<Border>("VideoDropZone")!, DragDrop.DropEvent, data); Pump(view.InputTask, 120); Pump(vm.ProbeVideoAsync());
                vm.OutputDirectory = Path.Combine(root, name); Directory.CreateDirectory(vm.OutputDirectory);
                var expectedAudio = vm.ExternalTracks.Count(t => t.IsAudio);
                check(vm.ExternalTracks.Count == (name == "multi-audio" ? 2 : added.Length) && vm.CanStart && vm.RequiresFonts == needFonts, name + " 真实文件自动识别并可提交");
                Pump(vm.StartAsync(), 180);
                check(vm.Result?.Status == MuxStatus.Succeeded && System.IO.File.Exists(vm.OutputPath), name + " 真实封装及全部成品校验通过：" + vm.Result?.Error);
                var probe = vm.Result?.Status == MuxStatus.Succeeded ? new SingleMuxService(workspace).ProbeAsync(vm.OutputPath) : throw new InvalidOperationException(vm.Result?.Error); Pump(probe);
                check(probe.Result.Tracks.Count(t => t.Type == "subtitles") == added.Count(MediaFileTypes.IsSubtitle) && probe.Result.Tracks.Count(t => t.Type == "audio") == 1 + expectedAudio, name + " 源音轨与新增轨道数量准确");
                check(!needFonts || probe.Result.Attachments.Count > 0, name + " 字体附件策略正确");
                if (!needFonts) check(probe.Result.Attachments.Count == 0 && vm.Result!.Checks.Any(c => c.Contains("SHA-256") || c.Contains("参考全文")), name + " 无字体流程也完成内容校验");
                if (name == "mixed") Save(window, Path.Combine(root, "mixed-success.png"));
                var producedPath = vm.OutputPath; var outputHash = SHA256.HashData(System.IO.File.ReadAllBytes(producedPath)); Pump(vm.StartAsync());
                check(vm.Result?.Status == MuxStatus.Failed && outputHash.SequenceEqual(SHA256.HashData(System.IO.File.ReadAllBytes(producedPath))), name + " 再次运行拒绝覆盖已有成品");
            }
            finally { Pump(vm.CancelAndWaitAsync()); window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        foreach (var corruptMetadata in new[] { true, false })
        {
            var output = Path.Combine(root, corruptMetadata ? "metadata-rejected" : "payload-rejected");
            var service = new SingleMuxService(workspace, new CorruptAudioRunner(corruptMetadata));
            var request = new MuxRequest(mkvVideo, [], "", output) { Audios = [new(audio, 0, "", "ja")] };
            var task = service.RunAsync(request); Pump(task, 90);
            check(task.Result.Status == MuxStatus.Failed && !System.IO.File.Exists(Path.Combine(output, "video.muxed.mkv")) && task.Result.Error!.Contains(corruptMetadata ? "元数据" : "数据与参考"), corruptMetadata ? "篡改新增音轨语言时 QC 阻止发布" : "篡改新增音轨 payload 时 SHA-256 阻止发布");
        }
        check(inputs.All(p => hashes[p].SequenceEqual(SHA256.HashData(System.IO.File.ReadAllBytes(p)))), "全部视频、音频、字幕和字体源哈希未改变");
    }
    private sealed class CorruptAudioRunner(bool metadata) : IMuxToolRunner
    {
        private readonly ProcessRunner runner = new();
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
        {
            var result = await runner.RunAsync(request, progress, cancellationToken);
            if (result.ExitCode != 0) return result;
            if (metadata && request.Arguments[0] == "-J" && Path.GetFileName(request.Arguments.Last()) == "result.mkv")
            {
                var json = JsonNode.Parse(result.StandardOutput)!;
                var added = json["tracks"]!.AsArray().Last(t => t!["type"]!.GetValue<string>() == "audio")!;
                added["properties"]!["language"] = "eng"; added["properties"]!["language_ietf"] = "en";
                return result with { StandardOutput = json.ToJsonString() };
            }
            if (!metadata && Path.GetFileName(request.Arguments[0]) == "result.mkv" && request.Arguments.Contains("--fullraw"))
            {
                var argument = request.Arguments.Last(); var path = argument[(argument.IndexOf(':') + 1)..];
                await using var file = new FileStream(path, FileMode.Append, FileAccess.Write); await file.WriteAsync(new byte[] { 123 }, cancellationToken);
            }
            return result;
        }
    }
    private sealed class ImportRunner : IMuxToolRunner
    {
        public bool Delay { get; set; } public bool Started { get; private set; }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
        {
            var name = Path.GetFileName(request.Arguments.Last());
            if (Delay) { Started = true; await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            if (name.Contains("broken")) return new(2, "", "损坏文件");
            var subtitle = MediaFileTypes.IsSubtitle(name); var audio = MediaFileTypes.IsAudio(name); var ass = MediaFileTypes.IsAss(name);
            var tracks = subtitle ? new[] { Track(0, "subtitles", ass ? "SubStationAlpha" : "SubRip/SRT", ass ? "S_TEXT/ASS" : "S_TEXT/UTF8") } : audio ? name.EndsWith(".mka") ? new[] { Track(2, "audio", "FLAC", "A_FLAC"), Track(9, "audio", "AAC", "A_AAC") } : new[] { Track(4, "audio", "FLAC", "A_FLAC") } : new[] { Track(0, "video", "AVC", "V_MPEG4/ISO/AVC"), Track(1, "audio", "AAC", "A_AAC") };
            return new(0, JsonSerializer.Serialize(new { container = new { properties = new { duration = 2_000_000_000L } }, tracks }), "");
        }
        private static object Track(int id, string type, string codec, string codecId) => new { id, type, codec, properties = new { codec_id = codecId, language = "und", track_name = "", audio_channels = 2, audio_sampling_frequency = 48000 } };
    }
    private const string Srt = "1\n00:00:00,000 --> 00:00:01,500\nHello 简繁字幕\n\n";
    private const string Ass = """
        [Script Info]
        ScriptType: v4.00+
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,1,1,0,2,10,10,10,1
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:01.50,Default,,0,0,0,,Hello
        """;
    private sealed record CheckResult(string Name, bool Passed);
}
