using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.Preferences;

namespace MuxDeck.Desktop.ViewModels;

public sealed partial class SingleMuxViewModel : ObservableObject
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
    private readonly ObservableCollection<TrackRowViewModel> sourceTracks = [], externalTracks = [];
    private readonly List<TrackRowViewModel> externalOrder = [];
    private long draftRevision, submittedRevision, resultRevision;
    private int configurationDepth;
    private bool configurationChanged, isCancelling;
    private string interactionMessage = "", progressMessage = "";
    private VideoProbeState probeState;
    private MuxStage? progressStage;
    private bool trackInspectorOpen, resultInspectorOpen;
    // 成品概览只读快照：绑定到具体成功 Result，迟到或被取代的探测结果不得回写。
    private MediaProbe? outputProbe;
    private CancellationTokenSource? outputProbeCancellation;
    private readonly List<Task> outputProbeTasks = [];
    private Task outputProbeTask = Task.CompletedTask;

    public TrackColumnLayout TrackColumns { get; }
    public InteractionNotification Notification { get; } = new();
    public MediaDropFeedback DropFeedback { get; } = new();
    public SingleMuxViewModel(SingleMuxService service, UserPreferencesStore? preferences = null)
    {
        this.service = service;
        TrackColumns = new(preferences);
        StartCommand = new(StartAsync, () => CanStart, ReportCommandError);
        CancelCommand = new(CancelAsync, () => CanCancel, ReportCommandError);
        SourceTracks = new(sourceTracks);
        ExternalTracks = new(externalTracks);
        // 两个固定槽位仅订阅一次，重建展示列表不会累积元数据订阅。
        foreach (var track in new[] { ScTrack, TcTrack })
        {
            track.SetEditGuard(() => CanEdit);
            track.PropertyChanged += SubtitlePropertyChanged;
        }
    }

    public TrackRowViewModel ScTrack { get; } = new() { Id = "SC", Type = "字幕", Codec = "ASS", Source = "外加 · 简体", Language = "zh-Hans", Name = "SC", IsDefault = true, IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hans。轨道名称、默认与强制旗标可编辑。" };
    public TrackRowViewModel TcTrack { get; } = new() { Id = "TC", Type = "字幕", Codec = "ASS", Source = "外加 · 繁体", Language = "zh-Hant", Name = "TC", IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hant。仅选择繁体时默认启用默认旗标。" };
    public string VideoPath { get => videoPath; set { if (!CanEdit || !Set(ref videoPath, value)) return; sourceProbe = null; SelectedTrack = null; RebuildTracks(false); Raise(nameof(ResolvedOutputFileName)); NotifyVideoSummary(); MarkDraftChanged(); UpdateInputs(); _ = ProbeVideoAsync(); } }
    public string ScSubtitle { get => scSubtitle; set => SetSubtitlePath(SubtitleSlot.Sc, value); }
    public string TcSubtitle { get => tcSubtitle; set => SetSubtitlePath(SubtitleSlot.Tc, value); }
    // 目录值变化同步菜单可用性和提交条件。
    public string FontsDirectory { get => fontsDirectory; set { if (CanEdit && Set(ref fontsDirectory, value)) { Raise(nameof(HasFontsDirectory)); MarkDraftChanged(); UpdateInputs(); } } }
    public string OutputDirectory { get => outputDirectory; set { if (CanEdit && Set(ref outputDirectory, value)) { Raise(nameof(HasOutputDirectory)); MarkDraftChanged(); UpdateInputs(); } } }
    public string OutputFileName { get => outputFileName; set { if (!CanEdit || !Set(ref outputFileName, value)) return; Raise(nameof(ResolvedOutputFileName)); MarkDraftChanged(); UpdateInputs(); } }
    public string ResolvedOutputFileName => string.IsNullOrWhiteSpace(OutputFileName) ? (string.IsNullOrWhiteSpace(VideoPath) ? "源文件名.muxed.mkv" : Path.GetFileNameWithoutExtension(VideoPath) + ".muxed.mkv") : OutputFileName;
    public bool IsRunning { get => isRunning; private set { if (!Set(ref isRunning, value)) return; NotifyEditability(); CancelCommand.NotifyCanExecuteChanged(); } }
    public bool IsProbing { get => isProbing; private set => Set(ref isProbing, value); }
    public bool CanEdit => !IsRunning && !isClosing;
    public bool RequiresFonts => externalOrder.Any(t => t.IsIncluded && t.IsSubtitle && MediaFileTypes.IsAss(t.InputPath));
    public bool HasEnabledExternal => externalOrder.Any(t => t.IsIncluded);
    public string FontCheckHint => RequiresFonts ? "开始封装时检查字体" : "仅 ASS 字幕需要字体目录";
    public bool CanStart => CanEdit && !string.IsNullOrWhiteSpace(VideoPath) && HasEnabledExternal && (!RequiresFonts || !string.IsNullOrWhiteSpace(FontsDirectory)) && !string.IsNullOrWhiteSpace(OutputDirectory);
    public string InputHint => IsRunning ? "本次参数已锁定。可切换页面；取消后等待工具退出与清理。" : CanStart ? "输入已齐全。开始时检查文件、工具与字体，目标存在时拒绝覆盖。" : "请选择视频、启用字幕或音频及输出目录；ASS 另需字体目录。";
    public string Log { get => log; private set => Set(ref log, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string StageText { get => stageText; private set { if (Set(ref stageText, value)) Raise(nameof(StateSummary)); } }
    public string ProbeText { get => probeText; private set { if (Set(ref probeText, value)) { Raise(nameof(VideoSummaryLine)); Raise(nameof(StatusDescription)); } } }
    public double Percent { get => percent; private set => Set(ref percent, value); }
    public bool IsIndeterminate { get => isIndeterminate; private set => Set(ref isIndeterminate, value); }
    public MuxResult? Result { get => result; private set { if (Set(ref result, value)) { resultRevision = submittedRevision; Raise(nameof(ResultSummary)); Raise(nameof(OutputPath)); Raise(nameof(LogPath)); NotifyState(); StartOutputProbe(value); } } }
    public string OutputPath => Result?.OutputPath ?? "";
    public string LogPath => Result?.LogPath ?? "";
    public string ResultSummary => Result is null ? "成品校验后才写入输出目录。" : FormatResultSummary(Result);
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];
    public ReadOnlyObservableCollection<TrackRowViewModel> SourceTracks { get; }
    public ReadOnlyObservableCollection<TrackRowViewModel> ExternalTracks { get; }
    public IReadOnlyList<MediaAttachment> SourceAttachments => sourceProbe?.Attachments ?? [];
    public int SourceAttachmentCount => SourceAttachments.Count;
    public int SourceTrackCount => sourceProbe?.Tracks.Count ?? 0;
    public string VideoFileName => Path.GetFileName(VideoPath);
    public string VideoFullPath => VideoPath;
    public string? VideoDimensions => sourceProbe?.Tracks.FirstOrDefault(t => t.Type == "video")?.PixelDimensions;
    public string? VideoCodec => sourceProbe?.Tracks.FirstOrDefault(t => t.Type == "video")?.Codec;
    public long? VideoDurationNanoseconds => sourceProbe?.DurationNanoseconds;
    public string MediaSummary => sourceProbe is null ? "媒体信息尚未取得" : string.Join(" · ",
        new[] { VideoDimensions, VideoCodec, VideoDurationNanoseconds is > 0 ? (VideoDurationNanoseconds.Value / 1_000_000_000d).ToString("0.###", CultureInfo.InvariantCulture) + " 秒" : null, $"{SourceTrackCount} 条轨道" }.Where(v => !string.IsNullOrWhiteSpace(v)));
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath);
    public string VideoDisplayName => HasVideo ? VideoFileName : "拖入一个视频，开始封装";
    public string? DisplayDuration => VideoDurationNanoseconds is > 0 ? FormatDuration(VideoDurationNanoseconds.Value) : null;
    public string DisplayMediaSummary => sourceProbe is null ? "尺寸、编码与时长将在探测后显示" : string.Join(" · ",
        new[] { VideoDimensions, VideoCodec, DisplayDuration, $"{SourceTrackCount} 条轨道" }.Where(v => !string.IsNullOrWhiteSpace(v)));
    private static string FormatDuration(long nanoseconds)
    {
        var seconds = nanoseconds / 1_000_000_000;
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
    public VideoProbeState ProbeState { get => probeState; private set { if (Set(ref probeState, value)) { Raise(nameof(VideoSummaryLine)); NotifyState(); } } }
    public long DraftRevision => draftRevision;
    public long? ResultRevision => Result is null ? null : resultRevision;
    public bool IsResultHistorical => Result is not null && (resultRevision != DraftRevision || IsRunning);
    public string ResultLabel => Result is null ? "尚无结果" : IsResultHistorical ? "上次结果" : "当前配置结果";
    public string InteractionMessage { get => interactionMessage; private set { if (Set(ref interactionMessage, value)) Raise(nameof(HasInteractionMessage)); } }
    public MuxStage? ProgressStage { get => progressStage; private set => Set(ref progressStage, value); }
    public string ProgressMessage { get => progressMessage; private set => Set(ref progressMessage, value); }
    public bool IsCancelling => isCancelling;
    public bool IsClosing => isClosing;
    public bool CanCancel => IsRunning && !IsCancelling && !IsClosing;
    public bool HasResult => Result is not null;
    public bool HasSuccessfulResult => Result?.Status == MuxStatus.Succeeded;
    public bool HasRawError => !string.IsNullOrWhiteSpace(Result?.Error);
    public string ResultOutcome => Result is null ? "" : FormatStatus(Result);
    public bool HasCurrentSuccess => HasSuccessfulResult && !IsResultHistorical;
    public string OutputDisplayName => Path.GetFileName(OutputPath);
    public string PrimaryActionText => HasCurrentSuccess ? "再次封装" : Result?.Status == MuxStatus.Failed && !IsResultHistorical ? "重试封装" : "开始封装";
    public bool HasWarnings => Result is not null && Result.Warnings.Count > 0;
    public string ResultHeadline => Result is null ? "" : Result.Status switch
    {
        MuxStatus.Succeeded when Result.Warnings.Count > 0 => "封装成功 · 附提示",
        MuxStatus.Succeeded => "全部校验通过",
        MuxStatus.Cancelled => "已取消",
        _ => "封装失败"
    };
    public string ResultHeaderNote => HasResult ? (IsResultHistorical ? "上次结果" : "当前配置结果") + " · " + ResultHeadline : "";
    public string OutputDirectoryDisplay => string.IsNullOrWhiteSpace(OutputPath) ? "" : (Path.GetDirectoryName(OutputPath) ?? "") + Path.DirectorySeparatorChar;
    // 成品计数只有在成功结果配套的只读探测返回后才展示；无来源时明确显示尚未取得。
    public bool HasOutputCounts => outputProbe is not null;
    public string OutputMediaCounts => outputProbe is null ? "成品概览尚未取得" : string.Join(" · ",
        new[] { outputProbe.Tracks.Count(t => t.Type == "video") + " 视频", outputProbe.Tracks.Count(t => t.Type == "audio") + " 音频", outputProbe.Tracks.Count(t => t.Type == "subtitles") + " 字幕" });
    public string OutputAttachmentSummary => outputProbe is null ? "" : $"{outputProbe.Attachments.Count} 个子集字体附件";
    public Task OutputProbeTask => outputProbeTask;
    public bool IsTrackInspectorOpen { get => trackInspectorOpen; private set { if (Set(ref trackInspectorOpen, value)) Raise(nameof(HasOpenInspector)); } }
    public bool IsResultInspectorOpen { get => resultInspectorOpen; private set { if (Set(ref resultInspectorOpen, value)) Raise(nameof(HasOpenInspector)); } }
    public bool HasOpenInspector => trackInspectorOpen || resultInspectorOpen;
    public bool HasInteractionMessage => !string.IsNullOrWhiteSpace(interactionMessage);
    public bool HasExternalTracks => externalTracks.Count > 0;
    public bool HasSourceAttachments => SourceAttachmentCount > 0;
    public string SourceGroupTitle => $"源视频 · {SourceTrackCount} 条";
    public string ExternalGroupTitle => $"外加内容 · {externalTracks.Count} 条";
    public string SourceAttachmentSummary => $"源附件（{SourceAttachmentCount}）";
    public bool HasFontsDirectory => !string.IsNullOrWhiteSpace(FontsDirectory);
    public bool HasOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);
    public string VideoSummaryLine => !HasVideo
        ? "拖入视频、音频、ASS / SRT 或字体目录，自动识别并落位。"
        : ProbeState switch { VideoProbeState.Probing => "正在读取源视频轨道…", VideoProbeState.Failed => ProbeText, _ => DisplayMediaSummary };
    public string StatusDescription => IsRunning ? "本次参数已锁定；取消后等待工具退出与清理。"
        : State == SingleMuxState.ProbeFailed ? ProbeText
        : State is SingleMuxState.Succeeded or SingleMuxState.SucceededWithWarnings ? "成品、校验与提示可在结果详情查看。"
        : State == SingleMuxState.Failed ? "失败原因、原始错误与日志在结果详情。"
        : State == SingleMuxState.Cancelled ? "已取消，未发布成品；输入保持可编辑。"
        : State == SingleMuxState.Ready ? "基本输入已齐全；开始时检查文件、字体与输出目标。"
        : MissingInputDescription;
    private IReadOnlyList<string> MissingInputs => new[]
    {
        string.IsNullOrWhiteSpace(VideoPath) ? "缺少源视频" : null,
        !HasEnabledExternal ? "缺少启用的字幕或音频" : null,
        RequiresFonts && string.IsNullOrWhiteSpace(FontsDirectory) ? "缺少字体目录" : null,
        string.IsNullOrWhiteSpace(OutputDirectory) ? "缺少输出目录" : null
    }.OfType<string>().ToArray();
    public string MissingInputDescription => string.Join(" · ", MissingInputs);
    public string ActionBarSummary => State == SingleMuxState.Incomplete ? $"还有 {MissingInputs.Count} 项需要处理" : State == SingleMuxState.Ready ? "已准备好" : StateSummary;
    public string ActionBarIcon => State switch
    {
        SingleMuxState.Ready or SingleMuxState.Succeeded => "✓",
        SingleMuxState.Incomplete or SingleMuxState.ProbeFailed or SingleMuxState.Failed or SingleMuxState.SucceededWithWarnings => "!",
        _ => ""
    };
    // 两个 Overlay 暂互斥：打开一个关闭另一个，主布局与固定状态区不重排。
    public void OpenTrackInspector() { IsResultInspectorOpen = false; IsTrackInspectorOpen = true; }
    public void CloseInspectors() { IsTrackInspectorOpen = false; IsResultInspectorOpen = false; }
    public void CloseResultInspector() => IsResultInspectorOpen = false;
    public void ToggleResultInspector()
    {
        if (IsResultInspectorOpen) { IsResultInspectorOpen = false; return; }
        IsTrackInspectorOpen = false; IsResultInspectorOpen = true;
    }
    public SingleMuxState State => isClosing ? SingleMuxState.Closing : IsRunning ? (isCancelling ? SingleMuxState.Cancelling : SingleMuxState.Running) :
        Result is not null && !IsResultHistorical ? Result.Status switch { MuxStatus.Cancelled => SingleMuxState.Cancelled, MuxStatus.Failed => SingleMuxState.Failed, _ => Result.Warnings.Count > 0 ? SingleMuxState.SucceededWithWarnings : SingleMuxState.Succeeded } :
        string.IsNullOrWhiteSpace(VideoPath) ? SingleMuxState.NoVideo : ProbeState == VideoProbeState.Probing ? SingleMuxState.Probing : ProbeState == VideoProbeState.Failed ? SingleMuxState.ProbeFailed : CanStart ? SingleMuxState.Ready : SingleMuxState.Incomplete;
    public string StateSummary => State switch
    {
        SingleMuxState.NoVideo => "请选择源视频", SingleMuxState.Probing => "正在探测视频", SingleMuxState.ProbeFailed => "视频探测失败，详情见探测消息",
        SingleMuxState.Ready => "输入已齐全，封装时检查", SingleMuxState.Incomplete => "输入不全，请补充启用轨道与输出设置",
        SingleMuxState.Running => StageText, SingleMuxState.Cancelling => "正在取消，等待工具退出与清理", SingleMuxState.Closing => "正在关闭，等待任务和探测结束",
        SingleMuxState.Cancelled => "已取消", SingleMuxState.Failed => "封装失败，请查看结果详情", SingleMuxState.SucceededWithWarnings => "封装成功，附提示", _ => "封装成功"
    };
    public TrackRowViewModel? SelectedTrack
    {
        get => selectedTrack;
        set
        {
            var previous = selectedTrack;
            if (!Set(ref selectedTrack, value)) return;
            if (previous is not null) previous.IsSelected = false;
            if (value is not null) value.IsSelected = true;
            Raise(nameof(HasSelectedTrack)); Raise(nameof(CanEditSelectedTrack));
            Raise(nameof(SelectedTrackIsExternal)); Raise(nameof(SelectedTrackFilePath));
        }
    }
    public bool HasSelectedTrack => SelectedTrack is not null;
    public bool SelectedTrackIsExternal => SelectedTrack?.IsEditable == true;
    public string SelectedTrackFilePath => SelectedTrack is null ? "" : SelectedTrack.IsEditable ? SelectedTrack.InputPath : VideoFullPath;
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
        ReportInteractionMessage("操作失败：" + ex.Message);
    }

    public void ReportInteractionMessage(string message, NotificationKind kind = NotificationKind.Error)
    {
        InteractionMessage = message;
        Notification.Show(message, kind);
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(ActionBarSummary), nameof(ActionBarIcon), nameof(MissingInputDescription), nameof(State), nameof(StateSummary), nameof(IsResultHistorical), nameof(ResultLabel), nameof(ResultRevision), nameof(CanCancel), nameof(HasResult), nameof(HasSuccessfulResult), nameof(HasRawError), nameof(HasCurrentSuccess), nameof(ResultOutcome), nameof(OutputDisplayName), nameof(PrimaryActionText), nameof(HasWarnings), nameof(ResultHeadline), nameof(ResultHeaderNote), nameof(OutputDirectoryDisplay), nameof(StatusDescription) }) Raise(name);
        CancelCommand.NotifyCanExecuteChanged();
    }
    private void UpdateInputs() { Raise(nameof(RequiresFonts)); Raise(nameof(HasEnabledExternal)); Raise(nameof(FontCheckHint)); Raise(nameof(CanStart)); Raise(nameof(InputHint)); StartCommand.NotifyCanExecuteChanged(); NotifyState(); }
    private void NotifyEditability()
    {
        Raise(nameof(CanEdit)); Raise(nameof(CanEditSelectedTrack));
        foreach (var row in externalOrder.Concat(new[] { ScTrack, TcTrack }).Distinct()) row.NotifyEditability(); UpdateInputs();
    }
    private void NotifyVideoSummary()
    {
        foreach (var name in new[] { nameof(VideoFileName), nameof(VideoFullPath), nameof(VideoDimensions), nameof(VideoCodec), nameof(VideoDurationNanoseconds), nameof(MediaSummary), nameof(SourceAttachments), nameof(SourceAttachmentCount), nameof(SourceTrackCount), nameof(HasVideo), nameof(VideoDisplayName), nameof(DisplayDuration), nameof(DisplayMediaSummary), nameof(VideoSummaryLine), nameof(SourceGroupTitle), nameof(HasSourceAttachments), nameof(SourceAttachmentSummary), nameof(SelectedTrackFilePath) }) Raise(name);
    }
    private void MarkDraftChanged()
    {
        if (configurationDepth > 0) { configurationChanged = true; return; }
        draftRevision++;
        Raise(nameof(DraftRevision)); NotifyState();
        // 旧视图也不再把新草稿显示为已成功；完整结果和任务日志仍保留。
        if (IsResultHistorical) StatusText = "草稿已修改 · 上次结果：" + FormatStatus(Result!);
    }
    private void ChangeConfiguration(Action change)
    {
        configurationDepth++;
        try { change(); }
        finally
        {
            configurationDepth--;
            if (configurationDepth == 0 && configurationChanged) { configurationChanged = false; MarkDraftChanged(); }
        }
    }
    private void SubtitlePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TrackRowViewModel.Name) or nameof(TrackRowViewModel.IsDefault) or nameof(TrackRowViewModel.IsForced) or nameof(TrackRowViewModel.IsIncluded))
        { MarkDraftChanged(); UpdateInputs(); }
    }
    private TrackRowViewModel SlotTrack(SubtitleSlot slot) => slot == SubtitleSlot.Sc ? ScTrack : TcTrack;
    private string SlotPath(SubtitleSlot slot) => slot == SubtitleSlot.Sc ? ScSubtitle : TcSubtitle;
    private void SetSubtitlePath(SubtitleSlot slot, string value)
    {
        if (!CanEdit || SlotPath(slot) == value) return;
        ChangeConfiguration(() =>
        {
            var hadSubtitle = !string.IsNullOrWhiteSpace(SlotPath(slot));
            if (slot == SubtitleSlot.Sc) Set(ref scSubtitle, value, nameof(ScSubtitle)); else Set(ref tcSubtitle, value, nameof(TcSubtitle));
            SlotTrack(slot).InputPath = value;
            // 同槽替换（含移除）后所选轨道身份不变，面板路径要跟随真实 InputPath 刷新。
            if (ReferenceEquals(SelectedTrack, SlotTrack(slot))) Raise(nameof(SelectedTrackFilePath));
            if (hadSubtitle != !string.IsNullOrWhiteSpace(value))
            {
                if (hadSubtitle) externalOrder.Remove(SlotTrack(slot)); else externalOrder.Add(SlotTrack(slot));
                SlotTrack(slot).IsIncluded = true;
                ApplySubtitleDefaults();
            }
            MarkDraftChanged(); RebuildTracks(); UpdateInputs();
        });
    }
    public InputAcceptanceResult RemoveSubtitle(SubtitleSlot slot)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, "当前正在运行、取消清理或关闭等待，不能修改输入。");
        if (!Enum.IsDefined(slot)) return Reject(InputAcceptanceError.InvalidSlot, "字幕槽位必须是 SC 或 TC。");
        SetSubtitlePath(slot, "");
        return AcceptMessage("已移除 " + slot.ToString().ToUpperInvariant() + " 字幕。");
    }
    public InputAcceptanceResult SetSubtitleIncluded(SubtitleSlot slot, bool included)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, "当前不能修改字幕启用状态。");
        if (!Enum.IsDefined(slot)) return Reject(InputAcceptanceError.InvalidSlot, "字幕槽位必须是 SC 或 TC。");
        if (string.IsNullOrWhiteSpace(SlotPath(slot))) return Reject(InputAcceptanceError.MissingPath, "该槽位尚未添加 ASS。");
        SlotTrack(slot).IsIncluded = included;
        return AcceptMessage(included ? "字幕已加入本次输出。" : "字幕暂不加入；路径、名称与旗标已保留。");
    }

    public InputAcceptanceResult PrepareVideoInput(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.Video, candidates, []);
    public InputAcceptanceResult PrepareSubtitleInputs(IReadOnlyList<SubtitleAssignment> assignments) => PrepareInput(InputOperation.Subtitles, [], assignments);
    public InputAcceptanceResult PrepareFontsDirectory(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.FontsDirectory, candidates, []);
    public InputAcceptanceResult PrepareOutputDirectory(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.OutputDirectory, candidates, []);
    private InputAcceptanceResult Reject(InputAcceptanceError error, string message) { ReportInteractionMessage(message); return new(error, message); }
    private InputAcceptanceResult AcceptMessage(string message) { ReportInteractionMessage(message, NotificationKind.Success); return new(InputAcceptanceError.None, message); }
    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return left == right;
        try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }
    private InputAcceptanceResult? ValidateCandidate(InputCandidate candidate, InputOperation operation, out InputCandidate normalized)
    {
        normalized = candidate;
        if (candidate.Kind == InputCandidateKind.NonLocal) return Reject(InputAcceptanceError.NonLocal, "只接受系统提供的本地文件或文件夹；URL 和纯文本路径不受支持。");
        var folder = operation is InputOperation.FontsDirectory or InputOperation.OutputDirectory;
        if (candidate.Kind != (folder ? InputCandidateKind.LocalDirectory : InputCandidateKind.LocalFile)) return Reject(InputAcceptanceError.WrongType, folder ? "请选择单个本地文件夹。" : "这里只接受本地文件。");
        var path = candidate.Path;
        // 接纳使用 Windows 绝对路径；旧直接属性赋值仍允许 Smoke 的内部假路径。
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains("://", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return Reject(InputAcceptanceError.InvalidPath, "需要完整的 Windows 本地路径。");
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.AsSpan(2).Contains(':')) return Reject(InputAcceptanceError.InvalidPath, "路径含有无效字符或数据流标记。");
            if (folder ? !Directory.Exists(path) : !File.Exists(path)) return Reject(InputAcceptanceError.MissingPath, folder ? "文件夹不存在或不可访问。" : "文件不存在或不可访问。");
            if (!folder)
            {
                var extension = Path.GetExtension(path);
                if (operation == InputOperation.Video ? !(extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)) : !extension.Equals(".ass", StringComparison.OrdinalIgnoreCase))
                    return Reject(InputAcceptanceError.WrongType, operation == InputOperation.Video ? "视频仅支持 MKV/MP4。" : "字幕仅支持 ASS。");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        { return Reject(InputAcceptanceError.InvalidPath, "路径无效或不可访问：" + ex.Message); }
        normalized = candidate with { Path = path };
        return null;
    }
    private InputAcceptanceResult PrepareInput(InputOperation operation, IReadOnlyList<InputCandidate> candidates, IReadOnlyList<SubtitleAssignment> assignments)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, "当前正在运行、取消清理或关闭等待，不能接纳输入。");
        var normalizedCandidates = new List<InputCandidate>();
        var normalizedAssignments = new List<SubtitleAssignment>();
        var requiresConfirmation = false;
        if (operation == InputOperation.Subtitles)
        {
            if (assignments.Count is < 1 or > 2) return Reject(InputAcceptanceError.InvalidCount, "一次接纳 1–2 个 ASS，并明确分配到 SC/TC。");
            foreach (var assignment in assignments)
            {
                if (!Enum.IsDefined(assignment.Slot) || normalizedAssignments.Any(a => a.Slot == assignment.Slot)) return Reject(InputAcceptanceError.InvalidSlot, "每个 SC/TC 槽位只能分配一次。");
                var error = ValidateCandidate(assignment.Candidate, operation, out var candidate);
                if (error is not null) return error;
                normalizedAssignments.Add(assignment with { Candidate = candidate });
                requiresConfirmation |= !string.IsNullOrWhiteSpace(SlotPath(assignment.Slot)) && !SamePath(SlotPath(assignment.Slot), candidate.Path);
            }
            // 与最终槽位集合比较；整组交换合法，不能拿将被替换的旧槽位误报重复。
            var finalSc = normalizedAssignments.FirstOrDefault(a => a.Slot == SubtitleSlot.Sc)?.Candidate.Path ?? ScSubtitle;
            var finalTc = normalizedAssignments.FirstOrDefault(a => a.Slot == SubtitleSlot.Tc)?.Candidate.Path ?? TcSubtitle;
            if (!string.IsNullOrWhiteSpace(finalSc) && !string.IsNullOrWhiteSpace(finalTc) && SamePath(finalSc, finalTc)) return Reject(InputAcceptanceError.Duplicate, "同一 ASS 路径不能同时分配到 SC 和 TC。");
        }
        else
        {
            if (candidates.Count != 1) return Reject(InputAcceptanceError.InvalidCount, operation == InputOperation.Video ? "一次只能接纳一个 MKV/MP4 视频。" : "一次只能接纳一个文件夹。");
            var error = ValidateCandidate(candidates[0], operation, out var candidate);
            if (error is not null) return error;
            normalizedCandidates.Add(candidate);
            requiresConfirmation = operation == InputOperation.Video && !string.IsNullOrWhiteSpace(VideoPath) && !SamePath(VideoPath, candidate.Path);
        }
        return new(InputAcceptanceError.None, requiresConfirmation ? "将替换已有输入，请明确确认。" : "候选有效，等待提交。",
            new(this, operation, DraftRevision, normalizedCandidates, normalizedAssignments, requiresConfirmation));
    }
    public InputAcceptanceResult CommitInput(InputAcceptancePlan plan, bool replacementsConfirmed = false)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, "当前正在运行、取消清理或关闭等待，不能提交输入。");
        if (!ReferenceEquals(plan.Owner, this) || plan.DraftRevision != DraftRevision) return Reject(InputAcceptanceError.Stale, "候选确认期间草稿已变化，请重新分配和确认。");
        // 选择器和确认框可能迟到；提交时重新检查全组存在性、重复和编辑锁。
        var validation = PrepareInput(plan.Operation, plan.Candidates, plan.Assignments);
        if (!validation.Succeeded) return validation;
        if (validation.Plan!.RequiresReplacementConfirmation && !replacementsConfirmed) return Reject(InputAcceptanceError.ConfirmationRequired, "替换已有视频或字幕需要明确确认。");
        ChangeConfiguration(() =>
        {
            if (plan.Operation == InputOperation.Subtitles)
            {
                foreach (var assignment in plan.Assignments)
                    if (!SamePath(SlotPath(assignment.Slot), assignment.Candidate.Path)) SetSubtitlePath(assignment.Slot, assignment.Candidate.Path);
            }
            else
            {
                var path = plan.Candidates[0].Path;
                switch (plan.Operation)
                {
                    case InputOperation.Video: if (!SamePath(VideoPath, path)) VideoPath = path; break;
                    case InputOperation.FontsDirectory: if (!SamePath(FontsDirectory, path)) FontsDirectory = path; break;
                    case InputOperation.OutputDirectory: if (!SamePath(OutputDirectory, path)) OutputDirectory = path; break;
                }
            }
        });
        return AcceptMessage(plan.Operation == InputOperation.FontsDirectory ? "字体目录已设置，封装时检查；尚未扫描或预检。" : "输入已接纳。");
    }
    private void ApplySubtitleDefaults()
    {
        // 只在输入组合改变时应用简繁约定，之后的显式旗标修改保留到提交快照。
        ScTrack.IsDefault = !string.IsNullOrWhiteSpace(ScSubtitle);
        TcTrack.IsDefault = string.IsNullOrWhiteSpace(ScSubtitle) && !string.IsNullOrWhiteSpace(TcSubtitle);
    }
    private void RebuildTracks(bool selectFallback = true)
    {
        var previous = SelectedTrack;
        Tracks.Clear();
        sourceTracks.Clear(); externalTracks.Clear();
        if (sourceProbe is not null)
        {
            foreach (var track in sourceProbe.Tracks)
            {
                var row = new TrackRowViewModel { Id = track.Id.ToString(), Type = track.Type switch { "video" => "视频", "audio" => "音频", "subtitles" => "字幕", _ => track.Type }, Codec = track.Codec, Source = "源视频 · 内封", Language = track.LanguageIetf ?? track.Language, LegacyLanguage = track.Language, LanguageIetf = track.LanguageIetf, CodecId = track.CodecId, PixelDimensions = track.PixelDimensions, AudioChannels = track.AudioChannels, Name = track.Name, IsDefault = track.IsDefault, IsForced = track.IsForced, HasDefaultFlag = track.HasDefaultFlag, HasForcedFlag = track.HasForcedFlag, Policy = track.Type is "video" or "audio" ? "保留" : "移除", Detail = $"{track.PixelDimensions ?? ""} { (track.AudioChannels is { } channels ? channels + " 声道" : "") } {track.CodecId}".Trim() };
                row.SetEditGuard(() => false);
                sourceTracks.Add(row); Tracks.Add(row);
            }
            foreach (var attachment in sourceProbe.Attachments)
                Tracks.Add(new TrackRowViewModel { Id = attachment.Id.ToString(), Type = "附件", Codec = attachment.ContentType, Source = "源视频 · 内封", Name = attachment.FileName, Policy = "移除", Detail = $"源附件 {attachment.Size:N0} 字节；成品只包含本次子集字体。" });
        }
        foreach (var row in externalOrder)
        {
            row.DisplayId = externalTracks.Count.ToString(CultureInfo.InvariantCulture);
            Tracks.Add(row); externalTracks.Add(row);
        }
        // 同一视频的源行随字幕变更重建，按来源/类型/轨道标识恢复到当前对象；换视频时调用方已清空选择。
        var retained = previous is null ? null : Tracks.Contains(previous) ? previous :
            Tracks.FirstOrDefault(t => !t.IsEditable && t.Id == previous.Id && t.Type == previous.Type && t.Source == previous.Source);
        SelectedTrack = retained ?? (selectFallback ? Tracks.FirstOrDefault(t => t.IsEditable) ?? Tracks.FirstOrDefault() : null);
        Raise(nameof(HasExternalTracks));
        Raise(nameof(ExternalGroupTitle));
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
        // 同路径重探测也撤下旧源信息；失败后不能把旧摘要当作当前媒体信息。
        sourceProbe = null;
        RebuildTracks(false);
        NotifyVideoSummary();
        IsProbing = true;
        ProbeState = string.IsNullOrWhiteSpace(path) ? VideoProbeState.Empty : VideoProbeState.Probing;
        ProbeText = "正在读取源视频轨道…";
        try
        {
            // 路径输入稍作合并，旧探测的迟到结果不得覆盖新视频。
            await Task.Delay(350, cancellation.Token);
            if (string.IsNullOrWhiteSpace(path)) { ProbeText = "选择视频后显示源轨道。"; return; }
            if (!File.Exists(path)) { ProbeText = "视频路径不存在，修正后自动重新探测。"; ProbeState = VideoProbeState.Failed; return; }
            var probe = await Task.Run(() => service.ProbeAsync(path, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || isClosing || !ReferenceEquals(probeCancellation, cancellation) || path != VideoPath) return;
            sourceProbe = probe;
            RebuildTracks();
            NotifyVideoSummary();
            ProbeState = VideoProbeState.Succeeded;
            ProbeText = $"源视频：{probe.Tracks.Count} 条轨道、{probe.Attachments.Count} 个附件。外加字幕显示为独立来源。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested && !isClosing && ReferenceEquals(probeCancellation, cancellation)) { ProbeState = VideoProbeState.Failed; ProbeText = "探测失败：" + ex.Message; AppendLog(ProbeText); } }
        finally
        {
            if (ReferenceEquals(probeCancellation, cancellation)) { probeCancellation = null; IsProbing = false; }
            cancellation.Dispose();
        }
    }

    private void StartOutputProbe(MuxResult? value)
    {
        // 换结果立即撤下不带来源的计数；只有成功结果才去只读探测成品。
        outputProbe = null;
        RaiseOutputCounts();
        if (value is not { Status: MuxStatus.Succeeded } || string.IsNullOrWhiteSpace(value.OutputPath) || isClosing) return;
        outputProbeTasks.RemoveAll(task => task.IsCompleted);
        outputProbeTask = ProbeOutputAsync(value, value.OutputPath);
        outputProbeTasks.Add(outputProbeTask);
    }
    private async Task ProbeOutputAsync(MuxResult owner, string path)
    {
        outputProbeCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        outputProbeCancellation = cancellation;
        try
        {
            var probe = await Task.Run(() => service.ProbeAsync(path, cancellation.Token), cancellation.Token);
            // 迟到或被更新的结果取代时不得把探测计数挂到别的成品上。
            if (cancellation.IsCancellationRequested || isClosing || !ReferenceEquals(Result, owner)) return;
            outputProbe = probe;
            RaiseOutputCounts();
        }
        // 成品探测失败只影响概览，不把已成功的 Core 结果改成失败。
        catch { }
        finally
        {
            if (ReferenceEquals(outputProbeCancellation, cancellation)) outputProbeCancellation = null;
            cancellation.Dispose();
        }
    }
    private void RaiseOutputCounts()
    {
        Raise(nameof(HasOutputCounts)); Raise(nameof(OutputMediaCounts)); Raise(nameof(OutputAttachmentSummary));
    }

    public Task StartAsync()
    {
        if (!CanStart) return Task.CompletedTask;
        var subtitles = new List<SubtitleInput>();
        if (!string.IsNullOrWhiteSpace(ScSubtitle) && ScTrack.IsIncluded) subtitles.Add(new(ScSubtitle, ScTrack.Name, ScTrack.Language, ScTrack.IsDefault, ScTrack.IsForced));
        if (!string.IsNullOrWhiteSpace(TcSubtitle) && TcTrack.IsIncluded) subtitles.Add(new(TcSubtitle, TcTrack.Name, TcTrack.Language, TcTrack.IsDefault, TcTrack.IsForced));
        subtitles.AddRange(externalOrder.Where(t => t.IsIncluded && t.IsSubtitle && t.Id is not ("SC" or "TC")).Select(t => new SubtitleInput(t.InputPath, t.Name, t.Language, t.IsDefault, t.IsForced)));
        var request = new MuxRequest(VideoPath, subtitles.ToArray(), FontsDirectory, OutputDirectory, string.IsNullOrWhiteSpace(OutputFileName) ? null : OutputFileName)
        { Audios = externalOrder.Where(t => t.IsIncluded && t.IsAudio).Select(t => new AudioInput(t.InputPath, t.OriginTrackId, t.Name, t.Language, t.IsDefault, t.IsForced)).ToArray() };
        // 此处构造本次不可变快照，执行中即使导航切页也不会改变提交参数。
        IsRunning = true;
        submittedRevision = DraftRevision;
        isCancelling = false; Raise(nameof(IsCancelling));
        StatusText = "运行中";
        StageText = "准备检查";
        ProgressStage = null; ProgressMessage = "准备检查";
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
                ProgressStage = value.Stage; ProgressMessage = value.Message;
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
        finally { IsIndeterminate = false; runCancellation = null; cancellation.Dispose(); isCancelling = false; Raise(nameof(IsCancelling)); IsRunning = false; }
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
    private Task CancelAsync() { runCancellation?.Cancel(); if (IsRunning) { isCancelling = true; Raise(nameof(IsCancelling)); NotifyState(); StatusText = "正在取消"; AppendLog("已请求取消，正在等待工具退出与清理。" ); } return Task.CompletedTask; }
    public async Task CancelAndWaitAsync()
    {
        isClosing = true;
        Raise(nameof(IsClosing)); NotifyEditability();
        probeCancellation?.Cancel();
        outputProbeCancellation?.Cancel();
        importCancellation.Cancel();
        await CancelAsync();
        await Task.WhenAll(RunTask, Task.WhenAll(probeTasks), Task.WhenAll(outputProbeTasks), Task.WhenAll(importTasks));
    }
    private sealed class UiProgress(Action<MuxProgress> update) : IProgress<MuxProgress>
    {
        public void Report(MuxProgress value) => Dispatcher.UIThread.Post(() => update(value));
    }
}
