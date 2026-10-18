using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Drafts;

public enum SubtitleSlot { Sc, Tc }

/// <summary>草稿接纳的操作类型；与界面按钮一一对应，不携带任何显示文案。</summary>
public enum DraftOperation { Video, Subtitles, FontsDirectory, OutputDirectory }

/// <summary>拒绝的结构化原因；中文文案由 Desktop 按（入口, 操作, 错误码, 原因）映射。</summary>
public enum DraftReason
{
    None,
    NonLocal,
    WrongLocationKindDirectory,
    WrongLocationKindFile,
    PathNotAbsolute,
    InvalidPathSyntax,
    PathInaccessible,
    DirectoryMissing,
    FileMissing,
    UnsupportedVideoExtension,
    UnsupportedSubtitleExtension
}

public static class DraftCodes
{
    public const string Locked = "MUX_DRAFT_LOCKED";
    public const string Stale = "MUX_DRAFT_STALE";
    public const string ConfirmationRequired = "MUX_DRAFT_CONFIRMATION_REQUIRED";
    public const string InvalidCount = "MUX_DRAFT_INVALID_COUNT";
    public const string InvalidSlot = "MUX_DRAFT_INVALID_SLOT";
    public const string Duplicate = "MUX_DRAFT_DUPLICATE";
    public const string NonLocal = "MUX_DRAFT_NON_LOCAL";
    public const string InvalidPath = "MUX_DRAFT_INVALID_PATH";
    public const string WrongType = "MUX_DRAFT_WRONG_TYPE";
    public const string MissingPath = "MUX_DRAFT_MISSING_PATH";
    public const string ExternalTrackMissing = "MUX_DRAFT_EXTERNAL_TRACK_MISSING";
}

/// <summary>草稿拒绝只给错误码与结构化原因；中文文案由 Desktop 映射，Application 不保存界面文字。</summary>
public sealed record DraftOutcome(bool Succeeded, string? ErrorCode, DraftAcceptancePlan? Plan = null,
    string? Detail = null, DraftReason Reason = DraftReason.None)
{
    public static DraftOutcome Refuse(string code, string? detail = null, DraftReason reason = DraftReason.None) =>
        new(false, code, null, detail, reason);
    public static DraftOutcome Accept() => new(true, null);
}

public sealed record DraftPrepareResult(bool Succeeded, string? ErrorCode, DraftAcceptancePlan? Plan = null,
    bool RequiresConfirmation = false, string? Detail = null, DraftReason Reason = DraftReason.None);

public sealed record DraftCandidate(InputItem Item);

public sealed record DraftAssignment(SubtitleSlot Slot, DraftCandidate Candidate);

/// <summary>外加轨道接纳快照；导入提交用它一次性写入整批轨道。</summary>
public sealed record DraftImportedTrack(string Identity, string Path, TrackKind Kind, TrackId? SourceTrackId,
    string Name, LanguageTag Language, bool HasDefaultFlag, bool IsDefault, bool IsForced);

public sealed record DraftImportRequest(string? VideoPath, string? FontsDirectory,
    IReadOnlyList<DraftImportedTrack> Tracks, bool VideoReplacementConfirmed = false);

/// <summary>接纳计划绑定草稿实例与修订号；弹窗期间草稿变化后提交必然过期。</summary>
public sealed class DraftAcceptancePlan
{
    internal DraftAcceptancePlan(MuxDraft owner, DraftOperation operation, long revision,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments,
        bool requiresConfirmation, string? normalizedCurrentPath = null, bool currentPathFactsAvailable = true,
        IReadOnlyDictionary<SubtitleSlot, (string Path, bool Available)>? normalizedCurrentSubtitlePaths = null)
    {
        Owner = owner;
        Operation = operation;
        Revision = revision;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        Assignments = Array.AsReadOnly(assignments.ToArray());
        RequiresReplacementConfirmation = requiresConfirmation;
        NormalizedCurrentPath = normalizedCurrentPath;
        CurrentPathFactsAvailable = currentPathFactsAvailable;
        NormalizedCurrentSubtitlePaths = normalizedCurrentSubtitlePaths ?? new Dictionary<SubtitleSlot, (string Path, bool Available)>();
    }

    internal MuxDraft Owner { get; }
    public DraftOperation Operation { get; }
    public long Revision { get; }
    public IReadOnlyList<DraftCandidate> Candidates { get; }
    public IReadOnlyList<DraftAssignment> Assignments { get; }
    public bool RequiresReplacementConfirmation { get; }
    internal string? NormalizedCurrentPath { get; }
    internal bool CurrentPathFactsAvailable { get; }
    internal IReadOnlyDictionary<SubtitleSlot, (string Path, bool Available)> NormalizedCurrentSubtitlePaths { get; }
}

public sealed record SubtitleSlotSnapshot(string Path, bool IsIncluded, string Name, LanguageTag Language,
    bool IsDefault, bool IsForced);

/// <summary>外加轨道的不可变投影；SourceTrackId 仅在音频导入时存在。</summary>
public sealed record DraftExternalTrackSnapshot(string Identity, string Path, TrackKind Kind,
    TrackId? SourceTrackId, string Name, LanguageTag Language, TrackFlags Flags)
{
    public bool IsIncluded { get; init; } = true;
}

/// <summary>草稿的只读整体状态；用于快照与“拒绝不改动”比对。</summary>
public sealed record DraftSnapshot(string VideoPath, SubtitleSlotSnapshot Sc, SubtitleSlotSnapshot Tc,
    string FontsDirectory, string OutputDirectory, string OutputFileName,
    EquatableArray<DraftExternalTrackSnapshot> ExternalTracks)
{
    public string PathFor(SubtitleSlot slot) => slot == SubtitleSlot.Sc ? Sc.Path : Tc.Path;
}

/// <summary>
/// 草稿会话模型：保存视频、SC/TC 槽位、外加轨道与输出设置，冻结修订号、编辑锁与准备→提交语义。
/// 修订号只在内容真正变化时增加；一次接纳批次（含嵌套）只算一次。需要文件系统事实时走 IPathFacts 端口。
/// 空草稿时 SC 默认启用，规则来自 Domain TrackPolicy；Application 不持有界面文字。
/// </summary>
public sealed class MuxDraft
{
    private readonly IPathFacts? pathFacts;
    private readonly ISyncPathFacts? syncPathFacts;
    private readonly List<MutableTrack> external = [];
    private readonly Dictionary<SubtitleSlot, MutableSubtitle> slots = new()
    {
        // 槽位名是写进成品的轨道名，不是界面标签；界面显示名由 Desktop 负责。
        [SubtitleSlot.Sc] = new MutableSubtitle("SC", new LanguageTag("zh-Hans"), TrackPolicy.InitialScDefault),
        [SubtitleSlot.Tc] = new MutableSubtitle("TC", new LanguageTag("zh-Hant"), TrackPolicy.InitialTcDefault)
    };
    private long revision;
    private int batchDepth;
    private bool batchDirty;
    private string videoPath = "", fontsDirectory = "", outputDirectory = "", outputFileName = "";
    private bool isLocked;

    /// <summary>异步端口用于 PrepareAsync/CommitAsync；同步端口用于 ViewModel 的同步入口，二者至少装配一个。</summary>
    public MuxDraft(IPathFacts? pathFacts = null, ISyncPathFacts? syncPathFacts = null)
    {
        if (pathFacts is null && syncPathFacts is null)
            throw new ArgumentException("草稿至少需要一个路径事实端口。", nameof(pathFacts));
        this.pathFacts = pathFacts;
        this.syncPathFacts = syncPathFacts;
    }

    private IPathFacts AsyncFacts => pathFacts ?? throw new InvalidOperationException("草稿未装配异步路径事实端口。");
    private ISyncPathFacts SyncFacts => syncPathFacts ?? throw new InvalidOperationException("草稿未装配同步路径事实端口。");

    /// <summary>草稿内容变化通知，参数是变化后的修订号；锁变化与同值赋值都不触发。</summary>
    public event EventHandler<long>? Changed;

    /// <summary>编辑锁变化通知：锁不改变草稿内容，因此不增加修订号、不触发 Changed。</summary>
    public event EventHandler? EditabilityChanged;

    public long DraftRevision => revision;
    public bool IsLocked => isLocked;
    public bool CanEdit => !isLocked;
    public string VideoPath => videoPath;
    public string? VideoBaseName => string.IsNullOrWhiteSpace(videoPath) ? null : Path.GetFileNameWithoutExtension(videoPath);
    public string FontsDirectory => fontsDirectory;
    public string OutputDirectory => outputDirectory;
    public string OutputFileName => outputFileName;
    public SubtitleSlotSnapshot Sc => SnapshotSlot(SubtitleSlot.Sc);
    public SubtitleSlotSnapshot Tc => SnapshotSlot(SubtitleSlot.Tc);
    public IReadOnlyList<DraftExternalTrackSnapshot> ExternalTracks => ProjectExternal();

    public SubtitleSlotSnapshot Slot(SubtitleSlot slot) =>
        IsKnownSlot(slot) ? SnapshotSlot(slot) : new("", false, "", default, false, false);
    public string PathFor(SubtitleSlot slot) => IsKnownSlot(slot) ? slots[slot].Path : "";
    public bool IsSlotIncluded(SubtitleSlot slot) => IsKnownSlot(slot) && slots[slot].IsIncluded;

    public DraftSnapshot Snapshot() => new(videoPath, Sc, Tc, fontsDirectory, outputDirectory, outputFileName, new(ProjectExternal()));

    /// <summary>锁定/解锁编辑；只影响后续修改是否被拒绝，不改变草稿内容或修订号。</summary>
    public void SetLocked(bool locked)
    {
        if (isLocked == locked) return;
        isLocked = locked;
        EditabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 把多个修改合并成一个批次；内部有真实改动时只增加一次修订号。
    /// 结算放在 finally，批次内抛异常时也已发生的改动同样结算，不残留未结算标记。
    /// </summary>
    public void Batch(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        batchDepth++;
        try { change(); }
        finally
        {
            batchDepth--;
            if (batchDepth == 0 && batchDirty) { batchDirty = false; MarkChanged(); }
        }
    }

    // ---------- 文档属性 ----------

    public DraftOutcome SetVideoPath(string value)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (videoPath == value) return DraftOutcome.Accept();
        videoPath = value;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetFontsDirectory(string value)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (fontsDirectory == value) return DraftOutcome.Accept();
        fontsDirectory = value;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetOutputDirectory(string value)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (outputDirectory == value) return DraftOutcome.Accept();
        outputDirectory = value;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetOutputFileName(string value)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (outputFileName == value) return DraftOutcome.Accept();
        outputFileName = value;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    // ---------- SC/TC 槽位 ----------

    public DraftOutcome SetSubtitlePath(SubtitleSlot slot, string path)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (!IsKnownSlot(slot)) return DraftOutcome.Refuse(DraftCodes.InvalidSlot);
        if (!ApplySlotPath(slot, path, ignoreCase: false)) return DraftOutcome.Accept();
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetSubtitleIncluded(SubtitleSlot slot, bool included)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (!IsKnownSlot(slot)) return DraftOutcome.Refuse(DraftCodes.InvalidSlot);
        // 空槽位没有可切换的输入，和旧 SetSubtitleIncluded 一样报缺路径。
        if (string.IsNullOrWhiteSpace(slots[slot].Path)) return DraftOutcome.Refuse(DraftCodes.MissingPath);
        var state = slots[slot];
        if (state.IsIncluded == included) return DraftOutcome.Accept();
        state.IsIncluded = included;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    /// <summary>行级直接编辑沿用旧行为：空槽位也允许切换纳入；公共入口仍先检查缺路径。</summary>
    public DraftOutcome SetSubtitleIncludedRaw(SubtitleSlot slot, bool included)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (!IsKnownSlot(slot)) return DraftOutcome.Refuse(DraftCodes.InvalidSlot);
        var state = slots[slot];
        if (state.IsIncluded == included) return DraftOutcome.Accept();
        state.IsIncluded = included;
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetSubtitleName(SubtitleSlot slot, string name) => SetSlotField(slot, name, static (state, value) => state.Name = value);
    public DraftOutcome SetSubtitleDefault(SubtitleSlot slot, bool value) => SetSlotField(slot, value, static (state, v) => state.IsDefault = v);
    public DraftOutcome SetSubtitleForced(SubtitleSlot slot, bool value) => SetSlotField(slot, value, static (state, v) => state.IsForced = v);

    // ---------- 有序外加轨道 ----------

    public DraftOutcome AddExternalTrack(DraftExternalTrackSnapshot track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        // 身份相同的重复添加仍然拒绝；跨身份按导入规则判断冲突。
        // 导入冲突规则：同路径，且已有轨道是字幕或两侧轨号相同；同一文件的不同音频轨号可共存。
        if (external.Any(item => item.Identity == track.Identity) ||
            ConflictsWithExisting(track, ProjectExternal(), includeSlots: true))
            return DraftOutcome.Refuse(DraftCodes.Duplicate);
        external.Add(new MutableTrack(track));
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome RemoveExternalTrack(string identity)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var removed = external.RemoveAll(track => track.Identity == identity);
        if (removed == 0) return DraftOutcome.Refuse(DraftCodes.ExternalTrackMissing);
        MarkChanged();
        return DraftOutcome.Accept();
    }

    /// <summary>替换外加轨道的文件与源轨标识，保留身份、名称、语言、旗标与纳入状态。</summary>
    public DraftOutcome ReplaceExternalTrack(string identity, DraftExternalTrackSnapshot replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var index = external.FindIndex(track => track.Identity == identity);
        if (index < 0) return DraftOutcome.Refuse(DraftCodes.ExternalTrackMissing);
        var current = external[index];
        var candidate = new DraftExternalTrackSnapshot(current.Identity, replacement.Path, current.Kind,
            replacement.SourceTrackId, current.Name, current.Language, new TrackFlags(current.IsDefault, current.IsForced))
        { IsIncluded = current.IsIncluded };
        var others = ProjectExternal().Where((_, otherIndex) => otherIndex != index).ToArray();
        if (ConflictsWithExisting(candidate, others, includeSlots: true))
            return DraftOutcome.Refuse(DraftCodes.Duplicate);
        external[index] = new MutableTrack(candidate);
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetExternalIncluded(string identity, bool included) =>
        SetExternalField(identity, included, static (track, value) => track.IsIncluded = value);

    public DraftOutcome SetExternalName(string identity, string name) =>
        SetExternalField(identity, name, static (track, value) => track.Name = value);

    public DraftOutcome SetExternalDefault(string identity, bool value) =>
        SetExternalField(identity, value, static (track, v) => track.IsDefault = v);

    public DraftOutcome SetExternalForced(string identity, bool value) =>
        SetExternalField(identity, value, static (track, v) => track.IsForced = v);

    /// <summary>
    /// 导入整批提交：先完成视频替换确认与全部轨道冲突检查，再在一个批次内写入；
    /// 任一项被拒时草稿与修订号都不变。字幕默认只在当前没有任何字幕行时设置，音频按源默认旗标。
    /// </summary>
    public DraftOutcome CommitImport(DraftImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var videoChanged = request.VideoPath is not null &&
            !SamePathWithFacts(videoPath, request.VideoPath, InputLocationKind.LocalFile);
        if (videoChanged && !string.IsNullOrWhiteSpace(videoPath) && !request.VideoReplacementConfirmed)
            return DraftOutcome.Refuse(DraftCodes.ConfirmationRequired);

        var seen = ConflictSnapshot().ToList();
        foreach (var track in request.Tracks)
        {
            var candidate = ToSnapshot(track, track.IsDefault);
            if (ConflictsWithExisting(candidate, seen, includeSlots: false))
                return DraftOutcome.Refuse(DraftCodes.Duplicate);
            seen.Add(candidate);
        }

        Batch(() =>
        {
            if (request.VideoPath is not null && !SamePathWithFacts(videoPath, request.VideoPath, InputLocationKind.LocalFile))
            { videoPath = request.VideoPath; MarkChanged(); }
            if (request.FontsDirectory is not null && !SamePathWithFacts(fontsDirectory, request.FontsDirectory, InputLocationKind.LocalDirectory))
            { fontsDirectory = request.FontsDirectory; MarkChanged(); }
            var hasSubtitle = HasAnySubtitle();
            foreach (var track in request.Tracks)
            {
                var isDefault = track.Kind == TrackKind.Subtitle
                    ? TrackPolicy.ImportedSubtitleIsDefault(hasSubtitle)
                    : TrackPolicy.ImportedAudioIsDefault(track.HasDefaultFlag, track.IsDefault);
                if (track.Kind == TrackKind.Subtitle) hasSubtitle = true;
                external.Add(new MutableTrack(ToSnapshot(track, isDefault)));
                MarkChanged();
            }
        });
        return DraftOutcome.Accept();
    }

    // ---------- 准备 → 提交 ----------

    /// <summary>准备接纳：默认经异步端口收集事实；调用方已持有事实时可传入，避免重复访问文件系统。</summary>
    public async Task<DraftPrepareResult> PrepareAsync(DraftOperation operation,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments,
        IReadOnlyList<InputPathFacts>? facts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(assignments);
        if (isLocked) return new(false, DraftCodes.Locked);
        var items = ItemsFor(operation, candidates, assignments);
        var actual = facts is not null
            ? new EquatableArray<InputPathFacts>(facts.ToArray())
            : await AsyncFacts.CollectSlotAsync(items, cancellationToken).ConfigureAwait(false);
        var current = await CollectCurrentAsync(operation, cancellationToken).ConfigureAwait(false);
        return InspectCore(operation, candidates, assignments, actual, current);
    }

    /// <summary>同步准备：ViewModel 的接纳 API 保持同步，收集与判定共用同一套事实与核心。</summary>
    public DraftPrepareResult Prepare(DraftOperation operation,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(assignments);
        if (isLocked) return new(false, DraftCodes.Locked);
        var items = ItemsFor(operation, candidates, assignments);
        var actual = new EquatableArray<InputPathFacts>(SyncFacts.Collect(items).ToArray());
        return InspectCore(operation, candidates, assignments, actual, CollectCurrentSync(operation));
    }

    /// <summary>
    /// 异步提交：重新核验锁、过期、是否已确认，并像旧 CommitInput 一样重新收集路径事实、
    /// 再跑一遍准备检查（存在、重复、类型）；是否需要确认按重新计算的结果判断。
    /// </summary>
    public async Task<DraftOutcome> CommitAsync(DraftAcceptancePlan plan, bool replacementsConfirmed = false,
        IReadOnlyList<InputPathFacts>? facts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        // 计划只属于创建它的草稿；否则视为过期，避免跨草稿误提交。
        if (!ReferenceEquals(plan.Owner, this)) return DraftOutcome.Refuse(DraftCodes.Stale);
        if (plan.Revision != revision) return DraftOutcome.Refuse(DraftCodes.Stale);
        var inspection = await PrepareAsync(plan.Operation, plan.Candidates, plan.Assignments, facts, cancellationToken).ConfigureAwait(false);
        return ApplyInspection(inspection, replacementsConfirmed);
    }

    /// <summary>同步提交：与异步入口共用 ApplyInspection，不维护第二份应用逻辑。</summary>
    public DraftOutcome Commit(DraftAcceptancePlan plan, bool replacementsConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (!ReferenceEquals(plan.Owner, this)) return DraftOutcome.Refuse(DraftCodes.Stale);
        if (plan.Revision != revision) return DraftOutcome.Refuse(DraftCodes.Stale);
        return ApplyInspection(Prepare(plan.Operation, plan.Candidates, plan.Assignments), replacementsConfirmed);
    }

    private DraftOutcome ApplyInspection(DraftPrepareResult inspection, bool replacementsConfirmed)
    {
        if (!inspection.Succeeded) return DraftOutcome.Refuse(inspection.ErrorCode!, inspection.Detail, inspection.Reason);
        if (inspection.RequiresConfirmation && !replacementsConfirmed)
            return DraftOutcome.Refuse(DraftCodes.ConfirmationRequired);
        ApplyPlan(inspection.Plan!);
        return DraftOutcome.Accept();
    }

    private void ApplyPlan(DraftAcceptancePlan normalized)
    {
        Batch(() =>
        {
            switch (normalized.Operation)
            {
                case DraftOperation.Subtitles:
                    foreach (var assignment in normalized.Assignments)
                    {
                        var current = normalized.NormalizedCurrentSubtitlePaths.TryGetValue(assignment.Slot, out var path)
                            ? path : ("", true);
                        var same = current.Item2 && !string.IsNullOrWhiteSpace(current.Item1) &&
                            SamePath(current.Item1, assignment.Candidate.Item.Path);
                        if (!same && ApplySlotPath(assignment.Slot, assignment.Candidate.Item.Path, ignoreCase: true)) MarkChanged();
                    }
                    break;
                case DraftOperation.Video:
                    if (!normalized.CurrentPathFactsAvailable || !SamePath(normalized.NormalizedCurrentPath ?? "", normalized.Candidates[0].Item.Path))
                    { videoPath = normalized.Candidates[0].Item.Path; MarkChanged(); }
                    break;
                case DraftOperation.FontsDirectory:
                    if (!normalized.CurrentPathFactsAvailable || !SamePath(normalized.NormalizedCurrentPath ?? "", normalized.Candidates[0].Item.Path)) { fontsDirectory = normalized.Candidates[0].Item.Path; MarkChanged(); }
                    break;
                case DraftOperation.OutputDirectory:
                    if (!normalized.CurrentPathFactsAvailable || !SamePath(normalized.NormalizedCurrentPath ?? "", normalized.Candidates[0].Item.Path)) { outputDirectory = normalized.Candidates[0].Item.Path; MarkChanged(); }
                    break;
            }
        });
    }

    /// <summary>返回本次任务的不可变规格；之后修改草稿不会影响已返回的快照。</summary>
    public MuxJobSpec ToSpec(ToolConfiguration tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tracks = new List<ExternalTrackSpec>();
        // 旧 StartAsync 的顺序：SC → TC → 外加字幕 → 外加音频；旧入口的字幕不携带源轨标识。
        var sc = slots[SubtitleSlot.Sc];
        var tc = slots[SubtitleSlot.Tc];
        if (!string.IsNullOrWhiteSpace(sc.Path) && sc.IsIncluded)
            tracks.Add(new ExternalTrackSpec(sc.Path, TrackKind.Subtitle, null, sc.Name, sc.Language, new(sc.IsDefault, sc.IsForced)));
        if (!string.IsNullOrWhiteSpace(tc.Path) && tc.IsIncluded)
            tracks.Add(new ExternalTrackSpec(tc.Path, TrackKind.Subtitle, null, tc.Name, tc.Language, new(tc.IsDefault, tc.IsForced)));
        foreach (var track in external.Where(track => track.IsIncluded && track.Kind == TrackKind.Subtitle))
            tracks.Add(new ExternalTrackSpec(track.Path, TrackKind.Subtitle, null, track.Name, track.Language, new(track.IsDefault, track.IsForced)));
        foreach (var track in external.Where(track => track.IsIncluded && track.Kind == TrackKind.Audio))
            tracks.Add(new ExternalTrackSpec(track.Path, TrackKind.Audio, track.SourceTrackId, track.Name, track.Language, new(track.IsDefault, track.IsForced)));
        foreach (var track in external.Where(track => track.IsIncluded && track.Kind is not (TrackKind.Subtitle or TrackKind.Audio)))
            tracks.Add(new ExternalTrackSpec(track.Path, track.Kind, track.SourceTrackId, track.Name, track.Language, new(track.IsDefault, track.IsForced)));
        var fileName = string.IsNullOrWhiteSpace(outputFileName) ? null : outputFileName;
        return new(videoPath, new(tracks), [], new FontSpec(fontsDirectory), new OutputSpec(outputDirectory, fileName), tools);
    }

    /// <summary>
    /// 导入冲突检查用的当前快照：包含已排除的轨道与有路径的 SC/TC，不能直接用只含启用项的 ToSpec。
    /// </summary>
    public MuxJobSpec ToConflictSpec(ToolConfiguration tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tracks = new List<ExternalTrackSpec>();
        var sc = slots[SubtitleSlot.Sc];
        var tc = slots[SubtitleSlot.Tc];
        if (!string.IsNullOrWhiteSpace(sc.Path))
            tracks.Add(new ExternalTrackSpec(sc.Path, TrackKind.Subtitle, null, sc.Name, sc.Language, new(sc.IsDefault, sc.IsForced)));
        if (!string.IsNullOrWhiteSpace(tc.Path))
            tracks.Add(new ExternalTrackSpec(tc.Path, TrackKind.Subtitle, null, tc.Name, tc.Language, new(tc.IsDefault, tc.IsForced)));
        foreach (var track in external)
            tracks.Add(new ExternalTrackSpec(track.Path, track.Kind,
                track.Kind == TrackKind.Subtitle ? null : track.SourceTrackId,
                track.Name, track.Language, new(track.IsDefault, track.IsForced)));
        var fileName = string.IsNullOrWhiteSpace(outputFileName) ? null : outputFileName;
        return new(videoPath, new(tracks), [], new FontSpec(fontsDirectory), new OutputSpec(outputDirectory, fileName), tools);
    }

    // ---------- 内部：槽位与外轨状态 ----------

    private sealed class MutableSubtitle(string name, LanguageTag language, bool isDefault)
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = name;
        public LanguageTag Language { get; } = language;
        public bool IsIncluded { get; set; } = true;
        public bool IsDefault { get; set; } = isDefault;
        public bool IsForced { get; set; }
    }

    private sealed class MutableTrack(DraftExternalTrackSnapshot source)
    {
        public string Identity { get; } = source.Identity;
        public string Path { get; } = source.Path;
        public TrackKind Kind { get; } = source.Kind;
        public TrackId? SourceTrackId { get; } = source.SourceTrackId;
        public string Name { get; set; } = source.Name;
        public LanguageTag Language { get; } = source.Language;
        public bool IsIncluded { get; set; } = source.IsIncluded;
        public bool IsDefault { get; set; } = source.Flags.IsDefault;
        public bool IsForced { get; set; } = source.Flags.IsForced;
    }

    private static bool IsKnownSlot(SubtitleSlot slot) => slot is SubtitleSlot.Sc or SubtitleSlot.Tc;

    private SubtitleSlotSnapshot SnapshotSlot(SubtitleSlot slot)
    {
        var state = slots[slot];
        return new(state.Path, state.IsIncluded, state.Name, state.Language, state.IsDefault, state.IsForced);
    }

    private IReadOnlyList<DraftExternalTrackSnapshot> ProjectExternal() =>
        external.Select(track => new DraftExternalTrackSnapshot(track.Identity, track.Path, track.Kind,
            track.SourceTrackId, track.Name, track.Language, new TrackFlags(track.IsDefault, track.IsForced))
        { IsIncluded = track.IsIncluded }).ToArray();

    private DraftOutcome SetSlotField<T>(SubtitleSlot slot, T value, Action<MutableSubtitle, T> apply)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        if (!IsKnownSlot(slot)) return DraftOutcome.Refuse(DraftCodes.InvalidSlot);
        var state = slots[slot];
        var before = SnapshotSlot(slot);
        apply(state, value);
        if (SnapshotSlot(slot) == before) return DraftOutcome.Accept();
        MarkChanged();
        return DraftOutcome.Accept();
    }

    private DraftOutcome SetExternalField<T>(string identity, T value, Action<MutableTrack, T> apply)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var track = external.FirstOrDefault(item => item.Identity == identity);
        if (track is null) return DraftOutcome.Refuse(DraftCodes.ExternalTrackMissing);
        var before = new TrackFlags(track.IsDefault, track.IsForced);
        var name = track.Name;
        var included = track.IsIncluded;
        apply(track, value);
        if (before != new TrackFlags(track.IsDefault, track.IsForced) || name != track.Name || included != track.IsIncluded)
            MarkChanged();
        return DraftOutcome.Accept();
    }

    private void ApplySubtitleDefaults()
    {
        var defaults = TrackPolicy.SubtitleDefaults(!string.IsNullOrWhiteSpace(slots[SubtitleSlot.Sc].Path),
            !string.IsNullOrWhiteSpace(slots[SubtitleSlot.Tc].Path));
        slots[SubtitleSlot.Sc].IsDefault = defaults.Sc;
        slots[SubtitleSlot.Tc].IsDefault = defaults.Tc;
    }

    /// <summary>套用槽位路径；返回是否真的发生了变化。</summary>
    private bool ApplySlotPath(SubtitleSlot slot, string path, bool ignoreCase)
    {
        var state = slots[slot];
        if (ignoreCase ? SamePath(state.Path, path) : state.Path == path) return false;
        var hadSubtitle = !string.IsNullOrWhiteSpace(state.Path);
        state.Path = path;
        if (hadSubtitle != !string.IsNullOrWhiteSpace(path))
        {
            // 槽位从空变有、或从有变空时重置纳入状态，和旧 SetSubtitlePath 一致。
            state.IsIncluded = true;
            ApplySubtitleDefaults();
        }
        return true;
    }

    /// <summary>旧实现按 Windows 忽略大小写比较规范化路径；草稿沿用同一规则。</summary>
    private static bool SamePath(string left, string right) => InputRules.SameNormalizedPath(left, right, ignoreCase: true);

    private bool SamePathWithFacts(string left, string right, InputLocationKind kind)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return left == right;
        if (syncPathFacts is null) return SamePath(left, right);
        var facts = SyncFacts.Collect(new[] { new InputItem(left, kind), new InputItem(right, kind) });
        return facts.Count == 2 && facts.All(fact => string.IsNullOrWhiteSpace(fact.FailureDetail)) &&
            SamePath(facts[0].FullPath, facts[1].FullPath);
    }

    private void MarkChanged()
    {
        if (batchDepth > 0) { batchDirty = true; return; }
        revision++;
        Changed?.Invoke(this, revision);
    }

    // ---------- 内部：接纳校验 ----------

    private sealed record CurrentPathFacts(string Raw, string Normalized, bool Available,
        IReadOnlyDictionary<SubtitleSlot, (string Path, bool Available)> SubtitlePaths);

    private static InputItem[] ItemsFor(DraftOperation operation,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments) =>
        operation == DraftOperation.Subtitles
            ? assignments.Select(assignment => assignment.Candidate.Item).ToArray()
            : candidates.Select(candidate => candidate.Item).ToArray();

    /// <summary>纯同步核心：只消费已收集的候选事实与当前路径事实，异步/同步入口共用。</summary>
    private DraftPrepareResult InspectCore(DraftOperation operation,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments,
        EquatableArray<InputPathFacts> actual, CurrentPathFacts current)
    {
        if (!InputRules.AcceptsSelectionCount(actual.Count, operation == DraftOperation.Subtitles))
            return new(false, DraftCodes.InvalidCount);
        return operation == DraftOperation.Subtitles
            ? PrepareSubtitles(actual, assignments, current.SubtitlePaths)
            : PrepareSingle(operation, actual, candidates, current.Raw, current.Normalized, current.Available);
    }

    private async Task<CurrentPathFacts> CollectCurrentAsync(DraftOperation operation, CancellationToken cancellationToken)
    {
        var subtitlePaths = new Dictionary<SubtitleSlot, (string Path, bool Available)>();
        if (operation == DraftOperation.Subtitles)
        {
            var currentSlots = KnownSlots().Where(slot => !string.IsNullOrWhiteSpace(PathFor(slot))).ToArray();
            if (currentSlots.Length > 0)
            {
                var currentFacts = await AsyncFacts.CollectSlotAsync(
                    currentSlots.Select(slot => new InputItem(PathFor(slot), InputLocationKind.LocalFile)).ToArray(),
                    cancellationToken).ConfigureAwait(false);
                for (var index = 0; index < currentSlots.Length; index++)
                {
                    var available = index < currentFacts.Count && string.IsNullOrWhiteSpace(currentFacts[index].FailureDetail);
                    subtitlePaths[currentSlots[index]] = (available ? currentFacts[index].FullPath : PathFor(currentSlots[index]), available);
                }
            }
            return new("", "", true, subtitlePaths);
        }
        var currentRaw = CurrentSingleRaw(operation);
        if (string.IsNullOrWhiteSpace(currentRaw)) return new(currentRaw, "", true, subtitlePaths);
        var currentSingle = await AsyncFacts.CollectSlotAsync(
            new[] { new InputItem(currentRaw, operation == DraftOperation.Video ? InputLocationKind.LocalFile : InputLocationKind.LocalDirectory) },
            cancellationToken).ConfigureAwait(false);
        var currentAvailable = currentSingle.Count == 1 && string.IsNullOrWhiteSpace(currentSingle[0].FailureDetail);
        return new(currentRaw, currentAvailable ? currentSingle[0].FullPath : "", currentAvailable, subtitlePaths);
    }

    private CurrentPathFacts CollectCurrentSync(DraftOperation operation)
    {
        var subtitlePaths = new Dictionary<SubtitleSlot, (string Path, bool Available)>();
        if (operation == DraftOperation.Subtitles)
        {
            var currentSlots = KnownSlots().Where(slot => !string.IsNullOrWhiteSpace(PathFor(slot))).ToArray();
            if (currentSlots.Length > 0)
            {
                var currentFacts = SyncFacts.Collect(
                    currentSlots.Select(slot => new InputItem(PathFor(slot), InputLocationKind.LocalFile)).ToArray());
                for (var index = 0; index < currentSlots.Length; index++)
                {
                    var available = index < currentFacts.Count && string.IsNullOrWhiteSpace(currentFacts[index].FailureDetail);
                    subtitlePaths[currentSlots[index]] = (available ? currentFacts[index].FullPath : PathFor(currentSlots[index]), available);
                }
            }
            return new("", "", true, subtitlePaths);
        }
        var currentRaw = CurrentSingleRaw(operation);
        if (string.IsNullOrWhiteSpace(currentRaw)) return new(currentRaw, "", true, subtitlePaths);
        var currentSingle = SyncFacts.Collect(
            new[] { new InputItem(currentRaw, operation == DraftOperation.Video ? InputLocationKind.LocalFile : InputLocationKind.LocalDirectory) });
        var currentAvailable = currentSingle.Count == 1 && string.IsNullOrWhiteSpace(currentSingle[0].FailureDetail);
        return new(currentRaw, currentAvailable ? currentSingle[0].FullPath : "", currentAvailable, subtitlePaths);
    }

    private string CurrentSingleRaw(DraftOperation operation) => operation switch
    {
        DraftOperation.Video => videoPath,
        DraftOperation.FontsDirectory => fontsDirectory,
        DraftOperation.OutputDirectory => outputDirectory,
        _ => ""
    };

    private static IEnumerable<SubtitleSlot> KnownSlots()
    {
        yield return SubtitleSlot.Sc;
        yield return SubtitleSlot.Tc;
    }

    private DraftPrepareResult PrepareSingle(DraftOperation operation,
        EquatableArray<InputPathFacts> facts, IReadOnlyList<DraftCandidate> candidates,
        string currentRawPath, string currentPath, bool currentPathAvailable)
    {
        if (facts.Count != 1) return new(false, DraftCodes.InvalidCount);
        var expectsDirectory = operation != DraftOperation.Video;
        if (ValidateFact(facts[0], expectsDirectory, operation == DraftOperation.Video) is { } error)
            return new(false, error.Code, Detail: error.Detail, Reason: error.Reason);
        var candidate = candidates[0] with { Item = new InputItem(facts[0].FullPath, facts[0].Kind) };
        var samePath = currentPathAvailable && SamePath(currentPath, facts[0].FullPath);
        var requiresConfirmation = operation == DraftOperation.Video && InputRules.RequiresReplacement(currentRawPath, samePath);
        return new(true, null, new DraftAcceptancePlan(this, operation, revision,
            new List<DraftCandidate> { candidate }, new List<DraftAssignment>(), requiresConfirmation,
            currentPathAvailable ? currentPath : null, currentPathAvailable), requiresConfirmation);
    }

    private DraftPrepareResult PrepareSubtitles(EquatableArray<InputPathFacts> facts,
        IReadOnlyList<DraftAssignment> assignments, IReadOnlyDictionary<SubtitleSlot, (string Path, bool Available)> currentPaths)
    {
        if (!InputRules.AcceptsSelectionCount(assignments.Count, subtitleSlots: true) || facts.Count != assignments.Count)
            return new(false, DraftCodes.InvalidCount);
        var normalized = new List<DraftAssignment>();
        var requiresConfirmation = false;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            if (!InputRules.IsSubtitleSlotAvailable((int)assignment.Slot, normalized.Select(item => (int)item.Slot)))
                return new(false, DraftCodes.InvalidSlot);
            if (ValidateFact(facts[index], expectsDirectory: false, isVideo: false) is { } error)
                return new(false, error.Code, Detail: error.Detail, Reason: error.Reason);
            normalized.Add(new(assignment.Slot, assignment.Candidate with { Item = new InputItem(facts[index].FullPath, facts[index].Kind) }));
            var current = currentPaths.TryGetValue(assignment.Slot, out var normalizedCurrent)
                ? normalizedCurrent : ("", true);
            var currentPath = current.Item1;
            requiresConfirmation |= InputRules.RequiresReplacement(currentPath,
                current.Item2 && SamePath(currentPath, facts[index].FullPath));
        }
        // 与最终槽位集合比较；整组交换合法，不能拿将被替换的旧槽位误报重复。
        var sc = FinalSubtitlePath(SubtitleSlot.Sc, normalized, currentPaths);
        var tc = FinalSubtitlePath(SubtitleSlot.Tc, normalized, currentPaths);
        if (InputRules.SubtitlePathsConflict(sc.Path, tc.Path, sc.Available && tc.Available && SamePath(sc.Path, tc.Path)))
            return new(false, DraftCodes.Duplicate);
        return new(true, null, new DraftAcceptancePlan(this, DraftOperation.Subtitles, revision, new List<DraftCandidate>(), normalized,
            requiresConfirmation, normalizedCurrentSubtitlePaths: currentPaths), requiresConfirmation);
    }

    private (string Path, bool Available) FinalSubtitlePath(SubtitleSlot slot,
        IReadOnlyList<DraftAssignment> normalized, IReadOnlyDictionary<SubtitleSlot, (string Path, bool Available)> currentPaths)
    {
        var assignment = normalized.FirstOrDefault(item => item.Slot == slot);
        if (assignment is not null) return (assignment.Candidate.Item.Path, true);
        return currentPaths.TryGetValue(slot, out var current) ? current : (PathFor(slot), true);
    }

    /// <summary>
    /// 按旧 ValidateCandidate 的顺序判定单个候选：类型 → 完整路径（原始 Path）→ 规范化失败 →
    /// 规范化路径语法 → 存在性 → 扩展名。规范化失败只影响候选本身。
    /// </summary>
    private static (string Code, string? Detail, DraftReason Reason)? ValidateFact(InputPathFacts fact, bool expectsDirectory, bool isVideo)
    {
        var kindError = InputRules.CheckCandidateKind(fact.Kind, expectsDirectory);
        if (kindError == InputKindError.NonLocal) return (DraftCodes.NonLocal, null, DraftReason.NonLocal);
        if (kindError == InputKindError.WrongType)
            return (DraftCodes.WrongType, null, expectsDirectory ? DraftReason.WrongLocationKindDirectory : DraftReason.WrongLocationKindFile);
        // 旧 ValidateCandidate 用原始路径判断完整路径与设备前缀；规范化只用于后续语法与存在性。
        if (!InputRules.AcceptsFixedSlotPath(fact.Path, fact.IsFullyQualified))
            return (DraftCodes.InvalidPath, null, DraftReason.PathNotAbsolute);
        if (!string.IsNullOrWhiteSpace(fact.FailureDetail))
            return (DraftCodes.InvalidPath, fact.FailureDetail, DraftReason.PathInaccessible);
        if (!InputRules.AcceptsNormalizedPathSyntax(fact.FullPath, fact.ContainsInvalidPathChars))
            return (DraftCodes.InvalidPath, null, DraftReason.InvalidPathSyntax);
        if (expectsDirectory ? !fact.DirectoryExists : !fact.FileExists)
            return (DraftCodes.MissingPath, null, expectsDirectory ? DraftReason.DirectoryMissing : DraftReason.FileMissing);
        if (!expectsDirectory && !InputRules.AcceptsFixedSlotExtension(fact.Extension, isVideo))
            return (DraftCodes.WrongType, null, isVideo ? DraftReason.UnsupportedVideoExtension : DraftReason.UnsupportedSubtitleExtension);
        return null;
    }

    // ---------- 内部：导入冲突与默认值 ----------

    private static DraftExternalTrackSnapshot ToSnapshot(DraftImportedTrack track, bool isDefault) =>
        new(track.Identity, track.Path, track.Kind,
            track.Kind == TrackKind.Subtitle ? null : track.SourceTrackId,
            track.Name, track.Language, new TrackFlags(isDefault, track.IsForced));

    private bool ConflictsWithExisting(DraftExternalTrackSnapshot candidate,
        IEnumerable<DraftExternalTrackSnapshot> existing, bool includeSlots)
    {
        if (includeSlots)
        {
            foreach (var slot in KnownSlots())
            {
                if (string.IsNullOrWhiteSpace(slots[slot].Path)) continue;
                if (InputRules.ImportedTrackConflicts(SamePath(slots[slot].Path, candidate.Path), true, -1,
                    candidate.SourceTrackId?.Value ?? -1)) return true;
            }
        }
        return existing.Any(track => InputRules.ImportedTrackConflicts(SamePath(track.Path, candidate.Path),
            track.Kind == TrackKind.Subtitle, track.SourceTrackId?.Value ?? -1, candidate.SourceTrackId?.Value ?? -1));
    }

    private List<DraftExternalTrackSnapshot> ConflictSnapshot()
    {
        var tracks = new List<DraftExternalTrackSnapshot>();
        foreach (var slot in KnownSlots())
        {
            if (string.IsNullOrWhiteSpace(slots[slot].Path)) continue;
            tracks.Add(new(slot.ToString(), slots[slot].Path, TrackKind.Subtitle, null, slots[slot].Name,
                slots[slot].Language, new TrackFlags(slots[slot].IsDefault, slots[slot].IsForced))
            { IsIncluded = slots[slot].IsIncluded });
        }
        tracks.AddRange(ProjectExternal());
        return tracks;
    }

    private bool HasAnySubtitle() =>
        !string.IsNullOrWhiteSpace(slots[SubtitleSlot.Sc].Path) ||
        !string.IsNullOrWhiteSpace(slots[SubtitleSlot.Tc].Path) ||
        external.Any(track => track.Kind == TrackKind.Subtitle);
}
