using MuxDeck.Desktop.Composition;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;

internal static class UiSingleMuxContractChecks
{
    public static void VerifyRunningSingleVm(string artifactRoot, Action<bool, string> check)
    {
        var fixture = Path.Combine(artifactRoot, "single-vm-fixture");
        var workspaceRoot = Path.Combine(fixture, "workspace");
        var video = Path.Combine(fixture, "video.mkv");
        var subtitle = Path.Combine(fixture, "input.ass");
        var fonts = Path.Combine(fixture, "fonts");
        var output = Path.Combine(fixture, "output");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(fonts);
        Directory.CreateDirectory(output);
        File.WriteAllText(video, "fixture video", new UTF8Encoding(false));
        File.WriteAllText(subtitle, Ass, new UTF8Encoding(false));

        var toolRoot = Path.Combine(workspaceRoot, "bin");
        foreach (var relative in new[] { "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe",
                     "assfontsubset/AssFontSubset.Console.exe", "python/Scripts/pyftsubset.exe", "python/Scripts/ttx.exe" })
        {
            var path = Path.Combine(toolRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fake tool", new UTF8Encoding(false));
        }

        var runner = new BlockingMuxToolRunner();
        var model = new SingleMuxViewModel(AppComposition.CreateSingleService(new Workspace(workspaceRoot), runner), new TrackColumnLayout())
        {
            FontsDirectory = fonts,
            OutputDirectory = output,
            ScSubtitle = subtitle,
            VideoPath = video
        };
        WaitWithDispatcher(model.ProbeVideoAsync(), TimeSpan.FromSeconds(10));
        check(model.StartCommand.CanExecute(null), "单任务 ViewModel 接受完整输入");
        try
        {
            _ = model.StartAsync();
            check(runner.AfsStarted.Task.Wait(TimeSpan.FromSeconds(10)), "开始命令进入受控子集化阶段");
            check(model.IsRunning && !model.CanEdit && !model.CanEditSelectedTrack &&
                !model.StartCommand.CanExecute(null) && model.CancelCommand.CanExecute(null),
                "运行中锁定输入和轨道编辑并启用取消");
            var originalVideo = model.VideoPath;
            model.VideoPath = Path.Combine(fixture, "changed-while-running.mkv");
            check(model.VideoPath == originalVideo, "运行中拒绝修改输入路径");

            model.CancelCommand.Execute(null);
            WaitWithDispatcher(model.RunTask, TimeSpan.FromSeconds(10));
            check(model.Result?.Status == MuxStatus.Cancelled && model.StatusText == "已取消" && !model.IsRunning,
                "等待取消后显示明确终态");
            check(model.CanEdit && model.StartCommand.CanExecute(null), "普通取消完成后恢复输入编辑");
            check(!File.Exists(Path.Combine(output, "video.muxed.mkv")), "取消后未发布成品");
            check(model.Result?.LogPath is { } log && File.Exists(log), "取消结果保留 Core 任务日志");
        }
        finally
        {
            if (model.IsRunning)
            {
                try { model.CancelCommand.Execute(null); WaitWithDispatcher(model.RunTask, TimeSpan.FromSeconds(10)); }
                catch { }
            }
        }
    }

    private static void WaitWithDispatcher(Task task, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        if (!task.IsCompleted) throw new TimeoutException("ViewModel 任务没有在限定时间内结束。");
        task.GetAwaiter().GetResult();
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

    private sealed class BlockingMuxToolRunner : IMuxToolRunner
    {
        private const string ProbeJson = """
            {"tracks":[{"id":0,"type":"video","codec":"V_AV1","properties":{"codec_id":"V_AV1","language":"und","language_ietf":"und","track_name":"","default_track":true,"forced_track":false,"pixel_dimensions":"16x16"}}],"attachments":[]}
            """;
        public TaskCompletionSource<bool> AfsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
            CancellationToken cancellationToken)
        {
            if (request.Arguments.Contains("-J")) return new ProcessResult(0, ProbeJson, "");
            if (Path.GetFileName(request.Executable).StartsWith("AssFontSubset", StringComparison.OrdinalIgnoreCase) &&
                request.Arguments.Contains("--subset-backend"))
            {
                AfsStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new ProcessResult(0, "", "");
        }
    }
}
