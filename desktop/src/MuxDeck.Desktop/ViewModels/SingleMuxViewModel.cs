using MuxDeck.Domain;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Contracts;

namespace MuxDeck.Desktop.ViewModels;

public sealed partial class SingleMuxViewModel : ObservableObject
{
    private readonly SingleMuxService service;
    private readonly IProbeMedia prober;
    private readonly SingleMuxDraft draft;
    private ToolConfiguration tools;
    private readonly Queue<string> logLines = new();
    private CancellationTokenSource? runCancellation;
    private CancellationTokenSource? probeCancellation;
    private readonly List<Task> probeTasks = [];
    private bool projectingRows;
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
    private long submittedRevision, resultRevision;
    private bool isCancelling;
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
    public SingleMuxViewModel(SingleMuxService service, TrackColumnLayout columns)
    {
        this.service = service;
        prober = service.Prober;
        tools = service.Tools;
        TrackColumns = columns;
        // 草稿是唯一数据源；ViewModel 只保留展示投影与文件探测等视图状态。
        draft = service.CreateDraft();
        draft.Changed += (_, _) => OnDraftChanged();
        draft.EditabilityChanged += (_, _) => NotifyEditability();
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

    public void ConfigureTools(ToolConfiguration configuration) => tools = configuration;

    private async Task<MediaProbe> ProbeMediaAsync(string path, CancellationToken token) =>
        LegacyMuxContractConverter.ToLegacyProbe(await prober.ProbeAsync(path, tools, token).ConfigureAwait(false));

    public TrackRowViewModel ScTrack { get; } = new() { Id = "SC", Slot = SubtitleSlot.Sc, Type = "字幕", Codec = "ASS", Source = "外加 · 简体", Language = "zh-Hans", Name = "SC", IsDefault = TrackPolicy.InitialScDefault, IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hans。轨道名称、默认与强制旗标可编辑。" };
    public TrackRowViewModel TcTrack { get; } = new() { Id = "TC", Slot = SubtitleSlot.Tc, Type = "字幕", Codec = "ASS", Source = "外加 · 繁体", Language = "zh-Hant", Name = "TC", IsDefault = TrackPolicy.InitialTcDefault, IsEditable = true, Policy = "新增", Detail = "子集化后新增 ASS；固定语言 zh-Hant。仅选择繁体时默认启用默认旗标。" };
    public string VideoPath
    {
        get => draft.VideoPath;
        set
        {
            if (!CanEdit || draft.VideoPath == value) return;
            if (!draft.SetVideoPath(value).Succeeded) return;
            sourceProbe = null; SelectedTrack = null; RebuildTracks(false);
            Raise(nameof(VideoPath)); Raise(nameof(ResolvedOutputFileName)); NotifyVideoSummary(); UpdateInputs();
            _ = ProbeVideoAsync();
        }
    }
    public string ScSubtitle { get => draft.PathFor(DraftSlot.Sc); set => SetSubtitlePath(SubtitleSlot.Sc, value); }
    public string TcSubtitle { get => draft.PathFor(DraftSlot.Tc); set => SetSubtitlePath(SubtitleSlot.Tc, value); }
    // 目录值变化同步菜单可用性和提交条件。
    public string FontsDirectory
    {
        get => draft.FontsDirectory;
        set
        {
            if (!CanEdit || draft.FontsDirectory == value) return;
            if (!draft.SetFontsDirectory(value).Succeeded) return;
            Raise(nameof(FontsDirectory)); Raise(nameof(HasFontsDirectory)); UpdateInputs();
        }
    }
    public string OutputDirectory
    {
        get => draft.OutputDirectory;
        set
        {
            if (!CanEdit || draft.OutputDirectory == value) return;
            if (!draft.SetOutputDirectory(value).Succeeded) return;
            Raise(nameof(OutputDirectory)); Raise(nameof(HasOutputDirectory)); UpdateInputs();
        }
    }
    public string OutputFileName
    {
        get => draft.OutputFileName;
        set
        {
            if (!CanEdit || draft.OutputFileName == value) return;
            if (!draft.SetOutputFileName(value).Succeeded) return;
            Raise(nameof(OutputFileName)); Raise(nameof(ResolvedOutputFileName)); UpdateInputs();
        }
    }
    // 占位文案只属于界面；Domain 在无源视频时返回 null。
    public string ResolvedOutputFileName => OutputNaming.ResolvePreview(OutputFileName, draft.VideoBaseName)
        ?? OutputNaming.DefaultFileName("源文件名");
    public bool IsRunning { get => isRunning; private set { if (!Set(ref isRunning, value)) return; UpdateDraftLock(); CancelCommand.NotifyCanExecuteChanged(); } }
    public bool IsProbing { get => isProbing; private set => Set(ref isProbing, value); }
    private int activeToolProbes;
    public bool HasActiveToolProbes => Volatile.Read(ref activeToolProbes) > 0;
    private void BeginToolProbe() { Interlocked.Increment(ref activeToolProbes); Raise(nameof(HasActiveToolProbes)); }
    private void EndToolProbe() { Interlocked.Decrement(ref activeToolProbes); Raise(nameof(HasActiveToolProbes)); }
    public bool CanEdit => !IsRunning && !isClosing && !IsComponentUninstalling;
    public bool RequiresFonts => externalOrder.Any(t => t.IsIncluded && t.IsSubtitle && MediaFileTypes.IsAss(t.InputPath));
    public bool HasEnabledExternal => externalOrder.Any(t => t.IsIncluded);
    public string FontCheckHint => RequiresFonts ? "开始封装时检查字体" : "仅 ASS 字幕需要字体目录";
    private bool componentInstalling;
    private bool componentUninstalling;
    public bool IsComponentUninstalling
    {
        get => componentUninstalling;
        set
        {
            if (!Set(ref componentUninstalling, value)) return;
            UpdateDraftLock();
            Raise(nameof(ActionBarSummary));
        }
    }
    public bool IsComponentInstalling
    {
        get => componentInstalling;
        set
        {
            if (!Set(ref componentInstalling, value)) return;
            Raise(nameof(CanStart));
            Raise(nameof(ActionBarSummary));
            StartCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanStart => CanEdit && !IsComponentInstalling && !string.IsNullOrWhiteSpace(VideoPath) && HasEnabledExternal && (!RequiresFonts || !string.IsNullOrWhiteSpace(FontsDirectory)) && !string.IsNullOrWhiteSpace(OutputDirectory);
    public string InputHint => IsRunning ? "本次参数已锁定。可切换页面；取消后等待工具退出与清理。" : CanStart ? "输入已齐全。开始时检查文件、工具与字体，目标存在时拒绝覆盖。" : "请选择视频、启用字幕或音频及输出目录；ASS 另需字体目录。";
    public string Log { get => log; private set => Set(ref log, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    // 运行中 ActionBarSummary 取 StageText，阶段变化必须同时通知摘要，否则绑定停留在上次终态。
    public string StageText { get => stageText; private set { if (Set(ref stageText, value)) { Raise(nameof(StateSummary)); Raise(nameof(ActionBarSummary)); } } }
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
    public long DraftRevision => draft.DraftRevision;
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
    public string ActionBarSummary => IsComponentUninstalling ? "组件卸载中，请等待完成" : IsComponentInstalling ? "组件安装中，请等待完成" : State == SingleMuxState.Incomplete ? $"还有 {MissingInputs.Count} 项需要处理" : State == SingleMuxState.Ready ? "已准备好" : StateSummary;
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

    // 三个锁定来源任一变化都同步草稿锁；草稿的 EditabilityChanged 负责通知行与输入条件。
    private void UpdateDraftLock() => draft.SetLocked(!CanEdit);
    private void NotifyVideoSummary()
    {
        foreach (var name in new[] { nameof(VideoFileName), nameof(VideoFullPath), nameof(VideoDimensions), nameof(VideoCodec), nameof(VideoDurationNanoseconds), nameof(MediaSummary), nameof(SourceAttachments), nameof(SourceAttachmentCount), nameof(SourceTrackCount), nameof(HasVideo), nameof(VideoDisplayName), nameof(DisplayDuration), nameof(DisplayMediaSummary), nameof(VideoSummaryLine), nameof(SourceGroupTitle), nameof(HasSourceAttachments), nameof(SourceAttachmentSummary), nameof(SelectedTrackFilePath) }) Raise(name);
    }
    private void OnDraftChanged()
    {
        SyncRowsFromDraft();
        Raise(nameof(DraftRevision)); NotifyState();
        // 旧视图也不再把新草稿显示为已成功；完整结果和任务日志仍保留。
        if (IsResultHistorical) StatusText = "草稿已修改 · 上次结果：" + FormatStatus(Result!);
    }

    // 行是展示投影：草稿变化时只同步已有行，结构增删由调用入口负责，避免重建对象身份。
    private void SyncRowsFromDraft()
    {
        projectingRows = true;
        try
        {
            SyncSlotRow(ScTrack, draft.Sc);
            SyncSlotRow(TcTrack, draft.Tc);
            foreach (var row in externalOrder)
            {
                var track = draft.ExternalTracks.FirstOrDefault(item => item.Identity == row.Id);
                if (track is null) continue;
                row.InputPath = track.Path;
                row.Name = track.Name;
                row.IsDefault = track.Flags.IsDefault;
                row.IsForced = track.Flags.IsForced;
                row.IsIncluded = track.IsIncluded;
            }
        }
        finally { projectingRows = false; }
    }

    private static void SyncSlotRow(TrackRowViewModel row, DraftSlotState snapshot)
    {
        row.InputPath = snapshot.Path;
        row.Name = snapshot.Name;
        row.IsDefault = snapshot.IsDefault;
        row.IsForced = snapshot.IsForced;
        row.IsIncluded = snapshot.IsIncluded;
    }

    private void SubtitlePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (projectingRows) return;
        if (args.PropertyName is not (nameof(TrackRowViewModel.Name) or nameof(TrackRowViewModel.IsDefault)
            or nameof(TrackRowViewModel.IsForced) or nameof(TrackRowViewModel.IsIncluded))) return;
        if (sender is TrackRowViewModel row)
        {
            if (row.Slot is { } slot)
            {
                var target = Map(slot);
                var outcome = args.PropertyName switch
                {
                    nameof(TrackRowViewModel.Name) => draft.SetSubtitleName(target, row.Name),
                    nameof(TrackRowViewModel.IsDefault) => draft.SetSubtitleDefault(target, row.IsDefault),
                    nameof(TrackRowViewModel.IsForced) => draft.SetSubtitleForced(target, row.IsForced),
                    _ => draft.SetSubtitleIncludedRaw(target, row.IsIncluded)
                };
                if (!outcome.Succeeded) SyncRowsFromDraft();
            }
            else
            {
                var outcome = args.PropertyName switch
                {
                    nameof(TrackRowViewModel.Name) => draft.SetExternalName(row.Id, row.Name),
                    nameof(TrackRowViewModel.IsDefault) => draft.SetExternalDefault(row.Id, row.IsDefault),
                    nameof(TrackRowViewModel.IsForced) => draft.SetExternalForced(row.Id, row.IsForced),
                    _ => draft.SetExternalIncluded(row.Id, row.IsIncluded)
                };
                if (!outcome.Succeeded) SyncRowsFromDraft();
            }
        }
        UpdateInputs();
    }

    private TrackRowViewModel SlotTrack(SubtitleSlot slot) => slot == SubtitleSlot.Sc ? ScTrack : TcTrack;
    private string SlotPath(SubtitleSlot slot) => draft.PathFor(Map(slot));
    private static DraftSlot Map(SubtitleSlot slot) => slot switch
    {
        SubtitleSlot.Sc => DraftSlot.Sc,
        SubtitleSlot.Tc => DraftSlot.Tc,
        // 保留未知值交给草稿报 InvalidSlot。
        _ => DraftSlot.Unknown
    };

    private void SetSubtitlePath(SubtitleSlot slot, string value)
    {
        if (!CanEdit || SlotPath(slot) == value) return;
        var hadSubtitle = !string.IsNullOrWhiteSpace(SlotPath(slot));
        if (!draft.SetSubtitlePath(Map(slot), value).Succeeded) return;
        var row = SlotTrack(slot);
        row.InputPath = value;
        // 同槽替换（含移除）后所选轨道身份不变，面板路径要跟随真实 InputPath 刷新。
        if (ReferenceEquals(SelectedTrack, row)) Raise(nameof(SelectedTrackFilePath));
        if (hadSubtitle != !string.IsNullOrWhiteSpace(value))
        {
            if (hadSubtitle) externalOrder.Remove(row); else externalOrder.Add(row);
        }
        Raise(slot == SubtitleSlot.Sc ? nameof(ScSubtitle) : nameof(TcSubtitle));
        RebuildTracks(); UpdateInputs();
    }
    public InputAcceptanceResult RemoveSubtitle(SubtitleSlot slot)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, DraftText.Describe(DraftEntry.Remove, InputOperation.Subtitles, DraftErrorKind.Locked, DraftFailureReason.None, null));
        if (!InputRules.IsSubtitleSlot((int)slot))
            return Reject(InputAcceptanceError.InvalidSlot, DraftText.Describe(DraftEntry.Remove, InputOperation.Subtitles, DraftErrorKind.InvalidSlot, DraftFailureReason.None, null));
        SetSubtitlePath(slot, "");
        return AcceptMessage("已移除 " + slot.ToString().ToUpperInvariant() + " 字幕。");
    }
    public InputAcceptanceResult SetSubtitleIncluded(SubtitleSlot slot, bool included)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, DraftText.Describe(DraftEntry.SetIncluded, InputOperation.Subtitles, DraftErrorKind.Locked, DraftFailureReason.None, null));
        if (!InputRules.IsSubtitleSlot((int)slot))
            return Reject(InputAcceptanceError.InvalidSlot, DraftText.Describe(DraftEntry.SetIncluded, InputOperation.Subtitles, DraftErrorKind.InvalidSlot, DraftFailureReason.None, null));
        var outcome = draft.SetSubtitleIncluded(Map(slot), included);
        if (!outcome.Succeeded)
            return Reject(DraftText.ToError(outcome.Error),
                DraftText.Describe(DraftEntry.SetIncluded, InputOperation.Subtitles, outcome.Error, outcome.Reason, outcome.Detail));
        UpdateInputs();
        return AcceptMessage(included ? "字幕已加入本次输出。" : "字幕暂不加入；路径、名称与旗标已保留。");
    }

    public InputAcceptanceResult PrepareVideoInput(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.Video, candidates, []);
    public InputAcceptanceResult PrepareSubtitleInputs(IReadOnlyList<SubtitleAssignment> assignments) => PrepareInput(InputOperation.Subtitles, [], assignments);
    public InputAcceptanceResult PrepareFontsDirectory(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.FontsDirectory, candidates, []);
    public InputAcceptanceResult PrepareOutputDirectory(IReadOnlyList<InputCandidate> candidates) => PrepareInput(InputOperation.OutputDirectory, candidates, []);
    private InputAcceptanceResult Reject(InputAcceptanceError error, string message) { ReportInteractionMessage(message); return new(error, message); }
    private InputAcceptanceResult AcceptMessage(string message) { ReportInteractionMessage(message, NotificationKind.Success); return new(InputAcceptanceError.None, message); }

    private static DraftOperationKind Operation(InputOperation operation) => operation switch
    {
        InputOperation.Video => DraftOperationKind.Video,
        InputOperation.Subtitles => DraftOperationKind.Subtitles,
        InputOperation.FontsDirectory => DraftOperationKind.FontsDirectory,
        _ => DraftOperationKind.OutputDirectory
    };

    private static DraftCandidateItem Candidate(InputCandidate candidate) =>
        new(candidate.Path, candidate.LocationKind);

    private InputAcceptanceResult PrepareInput(InputOperation operation, IReadOnlyList<InputCandidate> candidates, IReadOnlyList<SubtitleAssignment> assignments)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked, DraftText.Describe(DraftEntry.Prepare, operation, DraftErrorKind.Locked, DraftFailureReason.None, null));
        var prepared = draft.Prepare(Operation(operation),
            candidates.Select(Candidate).ToArray(),
            assignments.Select(assignment => new DraftSlotAssignment(Map(assignment.Slot), Candidate(assignment.Candidate))).ToArray());
        if (!prepared.Succeeded)
            return Reject(DraftText.ToError(prepared.Error),
                DraftText.Describe(DraftEntry.Prepare, operation, prepared.Error, prepared.Reason, prepared.Detail));
        var message = prepared.RequiresConfirmation ? "将替换已有输入，请明确确认。" : "候选有效，等待提交。";
        return new(InputAcceptanceError.None, message, new InputAcceptancePlan(this, operation, prepared.Plan!));
    }

    public InputAcceptanceResult CommitInput(InputAcceptancePlan plan, bool replacementsConfirmed = false)
    {
        if (!CanEdit) return Reject(InputAcceptanceError.Locked,
            DraftText.Describe(DraftEntry.Commit, plan?.Operation ?? InputOperation.Video, DraftErrorKind.Locked, DraftFailureReason.None, null));
        if (!ReferenceEquals(plan.Owner, this) || plan.DraftRevision != DraftRevision)
            return Reject(InputAcceptanceError.Stale,
                DraftText.Describe(DraftEntry.Commit, plan.Operation, DraftErrorKind.Stale, DraftFailureReason.None, null));
        // 选择器和确认框可能迟到；提交时重新收集事实并跑同一校验核心。
        var videoBefore = draft.VideoPath;
        var fontsBefore = draft.FontsDirectory;
        var outputBefore = draft.OutputDirectory;
        var revisionBefore = DraftRevision;
        var outcome = draft.Commit(plan.DraftPlan, replacementsConfirmed);
        if (!outcome.Succeeded)
            return Reject(DraftText.ToError(outcome.Error),
                DraftText.Describe(DraftEntry.Commit, plan.Operation, outcome.Error, outcome.Reason, outcome.Detail));
        // 同值提交不重建、不重复探测；旧 SetSubtitlePath/直接属性赋值同样视为无害。
        if (DraftRevision != revisionBefore) SyncAfterCommit(plan, videoBefore, fontsBefore, outputBefore);
        return AcceptMessage(plan.Operation == InputOperation.FontsDirectory ? "字体目录已设置，封装时检查；尚未扫描或预检。" : "输入已接纳。");
    }

    private void SyncAfterCommit(InputAcceptancePlan plan, string videoBefore, string fontsBefore, string outputBefore)
    {
        switch (plan.Operation)
        {
            case InputOperation.Subtitles:
                foreach (var assignment in plan.Assignments)
                {
                    var row = SlotTrack(assignment.Slot);
                    row.InputPath = draft.PathFor(Map(assignment.Slot));
                    if (ReferenceEquals(SelectedTrack, row)) Raise(nameof(SelectedTrackFilePath));
                    var present = !string.IsNullOrWhiteSpace(row.InputPath);
                    if (present && !externalOrder.Contains(row)) externalOrder.Add(row);
                    if (!present) externalOrder.Remove(row);
                }
                Raise(nameof(ScSubtitle)); Raise(nameof(TcSubtitle));
                RebuildTracks();
                break;
            case InputOperation.Video when draft.VideoPath != videoBefore:
                sourceProbe = null; SelectedTrack = null; RebuildTracks(false);
                Raise(nameof(VideoPath)); Raise(nameof(ResolvedOutputFileName)); NotifyVideoSummary();
                _ = ProbeVideoAsync();
                break;
            case InputOperation.FontsDirectory when draft.FontsDirectory != fontsBefore:
                Raise(nameof(FontsDirectory)); Raise(nameof(HasFontsDirectory));
                break;
            case InputOperation.OutputDirectory when draft.OutputDirectory != outputBefore:
                Raise(nameof(OutputDirectory)); Raise(nameof(HasOutputDirectory));
                break;
        }
        UpdateInputs();
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
                var sourceRetained = TrackPolicy.RetainsSource(track.Type switch { "video" => TrackKind.Video, "audio" => TrackKind.Audio, "subtitles" => TrackKind.Subtitle, _ => TrackKind.Unknown });
                var row = new TrackRowViewModel { Id = track.Id.ToString(), Type = track.Type switch { "video" => "视频", "audio" => "音频", "subtitles" => "字幕", _ => track.Type }, Codec = track.Codec, Source = "源视频 · 内封", Language = track.LanguageIetf ?? track.Language, LegacyLanguage = track.Language, LanguageIetf = track.LanguageIetf, CodecId = track.CodecId, PixelDimensions = track.PixelDimensions, AudioChannels = track.AudioChannels, Name = track.Name, IsDefault = track.IsDefault, IsForced = track.IsForced, HasDefaultFlag = track.HasDefaultFlag, HasForcedFlag = track.HasForcedFlag, SourceRetained = sourceRetained, Policy = sourceRetained ? "保留" : "移除", Detail = $"{track.PixelDimensions ?? ""} { (track.AudioChannels is { } channels ? channels + " 声道" : "") } {track.CodecId}".Trim() };
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
        BeginToolProbe();
        try
        {
            // 路径输入稍作合并，旧探测的迟到结果不得覆盖新视频。
            await Task.Delay(350, cancellation.Token);
            if (string.IsNullOrWhiteSpace(path)) { ProbeText = "选择视频后显示源轨道。"; return; }
            if (!File.Exists(path)) { ProbeText = "视频路径不存在，修正后自动重新探测。"; ProbeState = VideoProbeState.Failed; return; }
            var probe = await Task.Run(() => ProbeMediaAsync(path, cancellation.Token), cancellation.Token);
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
            EndToolProbe();
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
        BeginToolProbe();
        try
        {
            var probe = await Task.Run(() => ProbeMediaAsync(path, cancellation.Token), cancellation.Token);
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
            EndToolProbe();
        }
    }
    private void RaiseOutputCounts()
    {
        Raise(nameof(HasOutputCounts)); Raise(nameof(OutputMediaCounts)); Raise(nameof(OutputAttachmentSummary));
    }

    public Task StartAsync()
    {
        if (!CanStart) return Task.CompletedTask;
        // 草稿直接生成规格，执行中即使导航切页也不会改变已提交参数。
        var spec = draft.ToSpec(tools);
        IsRunning = true;
        submittedRevision = DraftRevision;
        isCancelling = false; Raise(nameof(IsCancelling));
        StatusText = "运行中";
        StageText = "准备检查";
        ProgressStage = null; ProgressMessage = "准备检查";
        Percent = 0;
        IsIndeterminate = true;
        runCancellation = new();
        RunTask = RunCoreAsync(spec, tools, runCancellation);
        return RunTask;
    }
    private async Task RunCoreAsync(MuxJobSpec spec, ToolConfiguration configuration, CancellationTokenSource cancellation)
    {
        try
        {
            AppendLog("开始封装：" + Path.GetFileName(spec.VideoPath));
            var progress = new UiProgress(value =>
            {
                if (!IsRunning || cancellation.IsCancellationRequested || !ReferenceEquals(runCancellation, cancellation)) return;
                StageText = value.Stage switch { MuxStage.Validating => "检查输入", MuxStage.Probing => "探测轨道", MuxStage.Subsetting => "字体子集化", MuxStage.Muxing => "封装 MKV", MuxStage.Checking => "校验成品", MuxStage.Installing => "写入成品", _ => "完成" };
                ProgressStage = value.Stage; ProgressMessage = value.Message;
                IsIndeterminate = value.Percent is null;
                Percent = Math.Clamp(value.Percent ?? 0, 0, 100);
                AppendLog(value.Message);
            });
            Result = await Task.Run(() => service.RunSpecAsync(spec, configuration, progress, cancellation.Token));
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
        Raise(nameof(IsClosing)); UpdateDraftLock();
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
