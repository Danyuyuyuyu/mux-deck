using System.Collections.ObjectModel;
using Avalonia.Threading;
using MuxDeck.Core.Muxing;

namespace MuxDeck.Desktop.ViewModels;

public sealed class SingleMuxViewModel : ObservableObject
{
    private readonly SingleMuxService service;
    private readonly Queue<string> logLines = new();
    private CancellationTokenSource? runCancellation;
    private CancellationTokenSource? probeCancellation;
    private readonly List<Task> probeTasks = [];
    private string videoPath = "", scSubtitle = "", tcSubtitle = "", fontsDirectory = "", outputDirectory = "", outputFileName = "";
    private string log = "选择视频和至少一份 UTF-8 ASS，然后指定字体与输出目录。";
    private string statusText = "待开始", stageText = "尚未运行", probeText = "选择视频后显示源轨道。";
    private bool isRunning, isProbing, isClosing;
    private double percent;
    private bool isIndeterminate;
    private MediaProbe? sourceProbe;
    private TrackRowViewModel? selectedTrack;
    private MuxResult? result;

    public SingleMuxViewModel(SingleMuxService service)
    {
        this.service = service;
        StartCommand = new(StartAsync, () => CanStart, ReportCommandError);
        CancelCommand = new(CancelAsync, () => IsRunning, ReportCommandError);
    }

    public TrackRowViewModel ScTrack { get; } = new() { Id = "SC", Type = "字幕", Codec = "ASS", Source = "外加 · 简体", Language = "zh-Hans", Name = "SC", IsDefault = true, IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hans。轨道名称、默认与强制旗标可编辑。" };
    public TrackRowViewModel TcTrack { get; } = new() { Id = "TC", Type = "字幕", Codec = "ASS", Source = "外加 · 繁体", Language = "zh-Hant", Name = "TC", IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hant。仅选择繁体时默认启用默认旗标。" };
    public string VideoPath { get => videoPath; set { if (!CanEdit || !Set(ref videoPath, value)) return; sourceProbe = null; RebuildTracks(); Raise(nameof(ResolvedOutputFileName)); UpdateInputs(); _ = ProbeVideoAsync(); } }
    public string ScSubtitle { get => scSubtitle; set { var hadSubtitle = !string.IsNullOrWhiteSpace(scSubtitle); if (!CanEdit || !Set(ref scSubtitle, value)) return; if (hadSubtitle != !string.IsNullOrWhiteSpace(value)) ApplySubtitleDefaults(); RebuildTracks(); UpdateInputs(); } }
    public string TcSubtitle { get => tcSubtitle; set { var hadSubtitle = !string.IsNullOrWhiteSpace(tcSubtitle); if (!CanEdit || !Set(ref tcSubtitle, value)) return; if (hadSubtitle != !string.IsNullOrWhiteSpace(value)) ApplySubtitleDefaults(); RebuildTracks(); UpdateInputs(); } }
    public string FontsDirectory { get => fontsDirectory; set { if (CanEdit && Set(ref fontsDirectory, value)) UpdateInputs(); } }
    public string OutputDirectory { get => outputDirectory; set { if (CanEdit && Set(ref outputDirectory, value)) UpdateInputs(); } }
    public string OutputFileName { get => outputFileName; set { if (!CanEdit || !Set(ref outputFileName, value)) return; Raise(nameof(ResolvedOutputFileName)); UpdateInputs(); } }
    public string ResolvedOutputFileName => string.IsNullOrWhiteSpace(OutputFileName) ? (string.IsNullOrWhiteSpace(VideoPath) ? "源文件名.muxed.mkv" : Path.GetFileNameWithoutExtension(VideoPath) + ".muxed.mkv") : OutputFileName;
    public bool IsRunning { get => isRunning; private set { if (!Set(ref isRunning, value)) return; Raise(nameof(CanEdit)); Raise(nameof(CanEditSelectedTrack)); Raise(nameof(CanStart)); Raise(nameof(InputHint)); StartCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } }
    public bool IsProbing { get => isProbing; private set => Set(ref isProbing, value); }
    public bool CanEdit => !IsRunning && !isClosing;
    public bool CanStart => CanEdit && !string.IsNullOrWhiteSpace(VideoPath) && (!string.IsNullOrWhiteSpace(ScSubtitle) || !string.IsNullOrWhiteSpace(TcSubtitle)) && !string.IsNullOrWhiteSpace(FontsDirectory) && !string.IsNullOrWhiteSpace(OutputDirectory);
    public string InputHint => IsRunning ? "本次参数已锁定。可切换页面；取消后等待工具退出与清理。" : CanStart ? "输入已齐全。开始时检查文件、工具与字体，目标存在时拒绝覆盖。" : "请选择一个视频、至少一份 UTF-8 ASS、字体目录和独立输出目录。";
    public string Log { get => log; private set => Set(ref log, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string StageText { get => stageText; private set => Set(ref stageText, value); }
    public string ProbeText { get => probeText; private set => Set(ref probeText, value); }
    public double Percent { get => percent; private set => Set(ref percent, value); }
    public bool IsIndeterminate { get => isIndeterminate; private set => Set(ref isIndeterminate, value); }
    public MuxResult? Result { get => result; private set { if (Set(ref result, value)) { Raise(nameof(ResultSummary)); Raise(nameof(OutputPath)); Raise(nameof(LogPath)); } } }
    public string OutputPath => Result?.OutputPath ?? "";
    public string LogPath => Result?.LogPath ?? "";
    public string ResultSummary => Result is null ? "成品校验后才写入输出目录。" : FormatResultSummary(Result);
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];
    public TrackRowViewModel? SelectedTrack { get => selectedTrack; set { if (Set(ref selectedTrack, value)) { Raise(nameof(HasSelectedTrack)); Raise(nameof(CanEditSelectedTrack)); } } }
    public bool HasSelectedTrack => SelectedTrack is not null;
    public bool CanEditSelectedTrack => CanEdit && SelectedTrack?.IsEditable == true;
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public Task RunTask { get; private set; } = Task.CompletedTask;

    private static string FormatStatus(MuxResult value) => value.Status switch
    {
        MuxStatus.Succeeded when value.Warnings.Count > 0 =>
            $"成功（{value.Warnings.Select(warning => warning.Code).Distinct().Count()}类提示/累计{value.Warnings.Sum(warning => (long)warning.Occurrences)}次）",
        MuxStatus.Succeeded => "成功",
        MuxStatus.Cancelled => "已取消",
        _ => "失败"
    };

    private static string FormatResultSummary(MuxResult value)
    {
        var summary = value.Status switch
        {
            MuxStatus.Succeeded => $"已完成：{value.OutputPath}",
            MuxStatus.Cancelled => "任务已取消，未发布成品。",
            _ => $"失败：{value.Error}"
        };
        // 可继续的提示与校验结果分开显示，成功不等于工具没有调整过字幕。
        if (value.Warnings.Count > 0)
            summary += $"\n提示（{value.Warnings.Select(warning => warning.Code).Distinct().Count()}类，累计{value.Warnings.Sum(warning => (long)warning.Occurrences)}次）：\n" +
                string.Join("\n", value.Warnings.Select(warning => $"• {warning.Message}（{warning.Occurrences}次）"));
        if (value.Status == MuxStatus.Succeeded && value.Checks.Count > 0)
            summary += "\n校验结果：\n" + string.Join("\n", value.Checks.Select(check => "• " + check));
        return summary;
    }

    private void ReportCommandError(Exception ex)
    {
        AppendLog("操作失败：" + ex.Message);
        if (!IsRunning) { Result = new(MuxStatus.Failed, null, ex.Message, null, []); StatusText = StageText = "失败"; }
    }

    private void UpdateInputs() { Raise(nameof(CanStart)); Raise(nameof(InputHint)); StartCommand.NotifyCanExecuteChanged(); }
    private void ApplySubtitleDefaults()
    {
        // 只在输入组合改变时应用简繁约定，之后的显式旗标修改保留到提交快照。
        ScTrack.IsDefault = !string.IsNullOrWhiteSpace(ScSubtitle);
        TcTrack.IsDefault = string.IsNullOrWhiteSpace(ScSubtitle) && !string.IsNullOrWhiteSpace(TcSubtitle);
    }
    private void RebuildTracks()
    {
        var previous = SelectedTrack;
        Tracks.Clear();
        if (sourceProbe is not null)
        {
            foreach (var track in sourceProbe.Tracks)
                Tracks.Add(new TrackRowViewModel { Id = track.Id.ToString(), Type = track.Type switch { "video" => "视频", "audio" => "音频", "subtitles" => "字幕", _ => track.Type }, Codec = track.Codec, Source = "源视频 · 内封", Language = track.LanguageIetf ?? track.Language, Name = track.Name, IsDefault = track.IsDefault, IsForced = track.IsForced, HasDefaultFlag = track.HasDefaultFlag, HasForcedFlag = track.HasForcedFlag, Policy = track.Type is "video" or "audio" ? "保留" : "移除", Detail = $"{track.PixelDimensions ?? ""} { (track.AudioChannels is { } channels ? channels + " 声道" : "") } {track.CodecId}".Trim() });
            foreach (var attachment in sourceProbe.Attachments)
                Tracks.Add(new TrackRowViewModel { Id = attachment.Id.ToString(), Type = "附件", Codec = attachment.ContentType, Source = "源视频 · 内封", Name = attachment.FileName, Policy = "移除", Detail = $"源附件 {attachment.Size:N0} 字节；成品只包含本次子集字体。" });
        }
        if (!string.IsNullOrWhiteSpace(ScSubtitle)) Tracks.Add(ScTrack);
        if (!string.IsNullOrWhiteSpace(TcSubtitle)) Tracks.Add(TcTrack);
        SelectedTrack = previous is not null && Tracks.Contains(previous) ? previous : Tracks.FirstOrDefault(t => t.IsEditable) ?? Tracks.FirstOrDefault();
    }

    public Task ProbeVideoAsync()
    {
        if (isClosing) return Task.CompletedTask;
        probeTasks.RemoveAll(task => task.IsCompleted);
        var task = ProbeCoreAsync();
        probeTasks.Add(task);
        return task;
    }
    private async Task ProbeCoreAsync()
    {
        probeCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        probeCancellation = cancellation;
        var path = VideoPath;
        IsProbing = true;
        ProbeText = "正在读取源视频轨道…";
        try
        {
            // 路径输入稍作合并，旧探测的迟到结果不得覆盖新视频。
            await Task.Delay(350, cancellation.Token);
            if (string.IsNullOrWhiteSpace(path)) { ProbeText = "选择视频后显示源轨道。"; return; }
            if (!File.Exists(path)) { ProbeText = "视频路径不存在，修正后自动重新探测。"; return; }
            var probe = await Task.Run(() => service.ProbeAsync(path, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || isClosing) return;
            sourceProbe = probe;
            RebuildTracks();
            ProbeText = $"源视频：{probe.Tracks.Count} 条轨道、{probe.Attachments.Count} 个附件。外加字幕显示为独立来源。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) { ProbeText = "探测失败：" + ex.Message; AppendLog(ProbeText); } }
        finally
        {
            if (ReferenceEquals(probeCancellation, cancellation)) { probeCancellation = null; IsProbing = false; }
            cancellation.Dispose();
        }
    }

    public Task StartAsync()
    {
        if (!CanStart) return Task.CompletedTask;
        var subtitles = new List<SubtitleInput>();
        if (!string.IsNullOrWhiteSpace(ScSubtitle)) subtitles.Add(new(ScSubtitle, ScTrack.Name, ScTrack.Language, ScTrack.IsDefault, ScTrack.IsForced));
        if (!string.IsNullOrWhiteSpace(TcSubtitle)) subtitles.Add(new(TcSubtitle, TcTrack.Name, TcTrack.Language, TcTrack.IsDefault, TcTrack.IsForced));
        var request = new MuxRequest(VideoPath, subtitles.ToArray(), FontsDirectory, OutputDirectory, string.IsNullOrWhiteSpace(OutputFileName) ? null : OutputFileName);
        // 此处构造本次不可变快照，执行中即使导航切页也不会改变提交参数。
        IsRunning = true;
        Result = null;
        StatusText = "运行中";
        StageText = "准备检查";
        Percent = 0;
        IsIndeterminate = true;
        runCancellation = new();
        RunTask = RunCoreAsync(request, runCancellation);
        return RunTask;
    }
    private async Task RunCoreAsync(MuxRequest request, CancellationTokenSource cancellation)
    {
        try
        {
            AppendLog("开始封装：" + Path.GetFileName(request.VideoPath));
            var progress = new UiProgress(value =>
            {
                if (!IsRunning || cancellation.IsCancellationRequested || !ReferenceEquals(runCancellation, cancellation)) return;
                StageText = value.Stage switch { MuxStage.Validating => "检查输入", MuxStage.Probing => "探测轨道", MuxStage.Subsetting => "字体子集化", MuxStage.Muxing => "封装 MKV", MuxStage.Checking => "校验成品", MuxStage.Installing => "写入成品", _ => "完成" };
                IsIndeterminate = value.Percent is null;
                Percent = Math.Clamp(value.Percent ?? 0, 0, 100);
                AppendLog(value.Message);
            });
            Result = await Task.Run(() => service.RunAsync(request, progress, cancellation.Token));
            StatusText = FormatStatus(Result);
            StageText = Result.Status == MuxStatus.Succeeded ? "完成" : StatusText;
            IsIndeterminate = false;
            Percent = Result.Status == MuxStatus.Succeeded ? 100 : 0;
            AppendLog(ResultSummary);
        }
        catch (OperationCanceledException) { Result = new(MuxStatus.Cancelled, null, null, null, []); StatusText = StageText = "已取消"; AppendLog(ResultSummary); }
        catch (Exception ex) { Result = new(MuxStatus.Failed, null, ex.Message, null, []); StatusText = StageText = "失败"; AppendLog(ResultSummary); }
        finally { IsIndeterminate = false; runCancellation = null; cancellation.Dispose(); IsRunning = false; }
    }
    public void AppendLog(string message)
    {
        // 工具长输出只保留最近 180 行，每行截断；完整诊断由 Core 写入本次文件日志。
        foreach (var line in message.Replace("\r", "").Split('\n'))
        {
            logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {(line.Length > 1200 ? line[..1200] + "…" : line)}");
            while (logLines.Count > 180) logLines.Dequeue();
        }
        Log = string.Join(Environment.NewLine, logLines);
    }
    private Task CancelAsync() { runCancellation?.Cancel(); if (IsRunning) { StatusText = "正在取消"; AppendLog("已请求取消，正在等待工具退出与清理。" ); } return Task.CompletedTask; }
    public async Task CancelAndWaitAsync()
    {
        isClosing = true;
        Raise(nameof(CanEdit)); Raise(nameof(CanEditSelectedTrack)); StartCommand.NotifyCanExecuteChanged();
        probeCancellation?.Cancel();
        await CancelAsync();
        await Task.WhenAll(RunTask, Task.WhenAll(probeTasks));
    }
    private sealed class UiProgress(Action<MuxProgress> update) : IProgress<MuxProgress>
    {
        public void Report(MuxProgress value) => Dispatcher.UIThread.Post(() => update(value));
    }
}
