using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Drafts;

public enum SubtitleSlot { Sc, Tc }

/// <summary>草稿接纳的操作类型；与界面按钮一一对应，不携带任何显示文案。</summary>
public enum DraftOperation { Video, Subtitles, FontsDirectory, OutputDirectory }

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
}

/// <summary>草稿拒绝只给错误码；中文文案由 Desktop 按键映射，Application 不保存界面文字。</summary>
public sealed record DraftOutcome(bool Succeeded, string? ErrorCode, DraftAcceptancePlan? Plan = null)
{
    public static DraftOutcome Refuse(string code) => new(false, code);
    public static DraftOutcome Accept() => new(true, null);
}

public sealed record DraftPrepareResult(bool Succeeded, string? ErrorCode, DraftAcceptancePlan? Plan = null, bool RequiresConfirmation = false);

public sealed record DraftCandidate(InputItem Item);

public sealed record DraftAssignment(SubtitleSlot Slot, DraftCandidate Candidate);

/// <summary>接纳计划绑定草稿实例与修订号；弹窗期间草稿变化后提交必然过期。</summary>
public sealed class DraftAcceptancePlan
{
    internal DraftAcceptancePlan(MuxDraft owner, DraftOperation operation, long revision,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments, bool requiresConfirmation)
    {
        Owner = owner;
        Operation = operation;
        Revision = revision;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        Assignments = Array.AsReadOnly(assignments.ToArray());
        RequiresReplacementConfirmation = requiresConfirmation;
    }

    internal MuxDraft Owner { get; }
    public DraftOperation Operation { get; }
    public long Revision { get; }
    public IReadOnlyList<DraftCandidate> Candidates { get; }
    public IReadOnlyList<DraftAssignment> Assignments { get; }
    public bool RequiresReplacementConfirmation { get; }
}

public sealed record SubtitleSlotSnapshot(string Path, bool IsIncluded, bool IsDefault, bool IsForced);

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
    private readonly IPathFacts pathFacts;
    private readonly List<MutableTrack> external = [];
    private readonly Dictionary<SubtitleSlot, MutableSubtitle> slots = new()
    {
        [SubtitleSlot.Sc] = new MutableSubtitle("简体", new LanguageTag("zh-Hans"), TrackPolicy.InitialScDefault),
        [SubtitleSlot.Tc] = new MutableSubtitle("繁体", new LanguageTag("zh-Hant"), TrackPolicy.InitialTcDefault)
    };
    private long revision;
    private int batchDepth;
    private bool batchDirty;
    private string videoPath = "", fontsDirectory = "", outputDirectory = "", outputFileName = "";
    private bool isLocked;

    public MuxDraft(IPathFacts pathFacts) => this.pathFacts = pathFacts ?? throw new ArgumentNullException(nameof(pathFacts));

    /// <summary>草稿内容变化通知，参数是变化后的修订号；锁变化与同值赋值都不触发。</summary>
    public event EventHandler<long>? Changed;

    /// <summary>编辑锁变化通知：锁不改变草稿内容，因此不增加修订号、不触发 Changed。</summary>
    public event EventHandler? EditabilityChanged;

    public long DraftRevision => revision;
    public bool IsLocked => isLocked;
    public bool CanEdit => !isLocked;
    public string VideoPath => videoPath;
    public string FontsDirectory => fontsDirectory;
    public string OutputDirectory => outputDirectory;
    public string OutputFileName => outputFileName;
    public SubtitleSlotSnapshot Sc => SnapshotSlot(SubtitleSlot.Sc);
    public SubtitleSlotSnapshot Tc => SnapshotSlot(SubtitleSlot.Tc);
    public IReadOnlyList<DraftExternalTrackSnapshot> ExternalTracks => ProjectExternal();

    public SubtitleSlotSnapshot Slot(SubtitleSlot slot) => SnapshotSlot(slot);
    public string PathFor(SubtitleSlot slot) => slots[slot].Path;
    public bool IsSlotIncluded(SubtitleSlot slot) => slots[slot].IsIncluded;

    public DraftSnapshot Snapshot() => new(videoPath, Sc, Tc, fontsDirectory, outputDirectory, outputFileName, new(ProjectExternal()));

    /// <summary>锁定/解锁编辑；只影响后续修改是否被拒绝，不改变草稿内容或修订号。</summary>
    public void SetLocked(bool locked)
    {
        if (isLocked == locked) return;
        isLocked = locked;
        EditabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把多个修改合并成一个批次；内部有真实改动时只增加一次修订号。</summary>
    public void Batch(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        batchDepth++;
        try { change(); }
        finally { batchDepth--; }
        if (batchDepth == 0 && batchDirty) { batchDirty = false; MarkChanged(); }
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
        var state = slots[slot];
        if (state.Path == path) return DraftOutcome.Accept();
        var hadSubtitle = !string.IsNullOrWhiteSpace(state.Path);
        state.Path = path;
        if (hadSubtitle != !string.IsNullOrWhiteSpace(path)) ApplySubtitleDefaults();
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome SetSubtitleIncluded(SubtitleSlot slot, bool included)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
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
        external.Add(new MutableTrack(track));
        MarkChanged();
        return DraftOutcome.Accept();
    }

    public DraftOutcome RemoveExternalTrack(string identity)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var removed = external.RemoveAll(track => track.Identity == identity);
        if (removed == 0) return DraftOutcome.Accept();
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

    // ---------- 准备 → 提交 ----------

    /// <summary>准备接纳：默认经 IPathFacts 收集事实；调用方已持有事实时可传入，避免重复访问文件系统。</summary>
    public async Task<DraftPrepareResult> PrepareAsync(DraftOperation operation,
        IReadOnlyList<DraftCandidate> candidates, IReadOnlyList<DraftAssignment> assignments,
        IReadOnlyList<InputPathFacts>? facts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(assignments);
        if (isLocked) return new(false, DraftCodes.Locked);
        var items = operation == DraftOperation.Subtitles
            ? assignments.Select(assignment => assignment.Candidate.Item).ToArray()
            : candidates.Select(candidate => candidate.Item).ToArray();
        if (!InputRules.AcceptsSelectionCount(items.Length, operation == DraftOperation.Subtitles))
            return new(false, DraftCodes.InvalidCount);
        var actual = facts is not null
            ? new EquatableArray<InputPathFacts>(facts.ToArray())
            : await pathFacts.CollectAsync(items, cancellationToken).ConfigureAwait(false);
        return operation == DraftOperation.Subtitles
            ? PrepareSubtitles(actual, assignments)
            : PrepareSingle(operation, actual, candidates);
    }

    public DraftOutcome Commit(DraftAcceptancePlan plan, bool replacementsConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        // 计划只属于创建它的草稿；否则视为过期，避免跨草稿误提交。
        if (!ReferenceEquals(plan.Owner, this)) return DraftOutcome.Refuse(DraftCodes.Stale);
        if (plan.Revision != revision) return DraftOutcome.Refuse(DraftCodes.Stale);
        if (plan.RequiresReplacementConfirmation && !replacementsConfirmed)
            return DraftOutcome.Refuse(DraftCodes.ConfirmationRequired);
        Batch(() =>
        {
            switch (plan.Operation)
            {
                case DraftOperation.Subtitles:
                    foreach (var assignment in plan.Assignments)
                        ApplySlotPath(assignment.Slot, assignment.Candidate.Item.Path);
                    break;
                case DraftOperation.Video:
                    videoPath = plan.Candidates[0].Item.Path;
                    break;
                case DraftOperation.FontsDirectory:
                    fontsDirectory = plan.Candidates[0].Item.Path;
                    break;
                case DraftOperation.OutputDirectory:
                    outputDirectory = plan.Candidates[0].Item.Path;
                    break;
            }
        });
        return DraftOutcome.Accept();
    }

    /// <summary>返回本次任务的不可变规格；之后修改草稿不会影响已返回的快照。</summary>
    public MuxJobSpec ToSpec(ToolConfiguration tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var sc = slots[SubtitleSlot.Sc];
        var tc = slots[SubtitleSlot.Tc];
        var tracks = new List<ExternalTrackSpec>();
        // 固定槽位按 SC、TC 顺序，外加轨道保持加入顺序；旧入口的字幕不携带源轨标识。
        if (!string.IsNullOrWhiteSpace(sc.Path) && sc.IsIncluded)
            tracks.Add(new ExternalTrackSpec(sc.Path, TrackKind.Subtitle, null, sc.Name, sc.Language, new(sc.IsDefault, sc.IsForced)));
        if (!string.IsNullOrWhiteSpace(tc.Path) && tc.IsIncluded)
            tracks.Add(new ExternalTrackSpec(tc.Path, TrackKind.Subtitle, null, tc.Name, tc.Language, new(tc.IsDefault, tc.IsForced)));
        foreach (var track in external.Where(track => track.IsIncluded))
            tracks.Add(new ExternalTrackSpec(track.Path, track.Kind, track.SourceTrackId, track.Name,
                track.Language, new(track.IsDefault, track.IsForced)));
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

    private SubtitleSlotSnapshot SnapshotSlot(SubtitleSlot slot)
    {
        var state = slots[slot];
        return new(state.Path, state.IsIncluded, state.IsDefault, state.IsForced);
    }

    private IReadOnlyList<DraftExternalTrackSnapshot> ProjectExternal() =>
        external.Select(track => new DraftExternalTrackSnapshot(track.Identity, track.Path, track.Kind,
            track.SourceTrackId, track.Name, track.Language, new TrackFlags(track.IsDefault, track.IsForced))).ToArray();

    private DraftOutcome SetSlotField<T>(SubtitleSlot slot, T value, Action<MutableSubtitle, T> apply)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var state = slots[slot];
        var before = SnapshotSlot(slot);
        var name = state.Name;
        apply(state, value);
        // 槽位快照不含名称，名称变化必须单独比较，否则会被当成同值赋值。
        if (SnapshotSlot(slot) == before && name == state.Name) return DraftOutcome.Accept();
        MarkChanged();
        return DraftOutcome.Accept();
    }

    private DraftOutcome SetExternalField<T>(string identity, T value, Action<MutableTrack, T> apply)
    {
        if (isLocked) return DraftOutcome.Refuse(DraftCodes.Locked);
        var track = external.FirstOrDefault(item => item.Identity == identity);
        if (track is null) return DraftOutcome.Refuse(DraftCodes.InvalidPath);
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

    private void ApplySlotPath(SubtitleSlot slot, string path)
    {
        var state = slots[slot];
        if (state.Path == path) return;
        var hadSubtitle = !string.IsNullOrWhiteSpace(state.Path);
        state.Path = path;
        if (hadSubtitle != !string.IsNullOrWhiteSpace(path)) ApplySubtitleDefaults();
        // 槽位路径变化必须记入批次；同一批次里的多个槽位只算一次修订。
        MarkChanged();
    }

    private void MarkChanged()
    {
        if (batchDepth > 0) { batchDirty = true; return; }
        revision++;
        Changed?.Invoke(this, revision);
    }

    // ---------- 内部：接纳校验 ----------

    private DraftPrepareResult PrepareSingle(DraftOperation operation,
        EquatableArray<InputPathFacts> facts, IReadOnlyList<DraftCandidate> candidates)
    {
        if (facts.Count != 1) return new(false, DraftCodes.InvalidCount);
        var expectsDirectory = operation != DraftOperation.Video;
        if (ValidateFact(facts[0], expectsDirectory, operation == DraftOperation.Video) is { } error) return new(false, error);
        var candidate = candidates[0] with { Item = new InputItem(facts[0].FullPath, facts[0].Kind) };
        var requiresConfirmation = operation == DraftOperation.Video &&
            InputRules.RequiresReplacement(videoPath, facts[0].FullPath == videoPath);
        return new(true, null, new DraftAcceptancePlan(this, operation, revision,
            new List<DraftCandidate> { candidate }, new List<DraftAssignment>(), requiresConfirmation), requiresConfirmation);
    }

    private DraftPrepareResult PrepareSubtitles(EquatableArray<InputPathFacts> facts, IReadOnlyList<DraftAssignment> assignments)
    {
        if (!InputRules.AcceptsSelectionCount(assignments.Count, subtitleSlots: true)) return new(false, DraftCodes.InvalidCount);
        var normalized = new List<DraftAssignment>();
        var requiresConfirmation = false;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            if (!InputRules.IsSubtitleSlotAvailable((int)assignment.Slot, normalized.Select(item => (int)item.Slot)))
                return new(false, DraftCodes.InvalidSlot);
            if (ValidateFact(facts[index], expectsDirectory: false, isVideo: false) is { } error) return new(false, error);
            normalized.Add(new(assignment.Slot, assignment.Candidate with { Item = new InputItem(facts[index].FullPath, facts[index].Kind) }));
            requiresConfirmation |= InputRules.RequiresReplacement(PathFor(assignment.Slot), facts[index].FullPath == PathFor(assignment.Slot));
        }
        // 与最终槽位集合比较；整组交换合法，不能拿将被替换的旧槽位误报重复。
        var finalSc = normalized.FirstOrDefault(item => item.Slot == SubtitleSlot.Sc)?.Candidate.Item.Path ?? PathFor(SubtitleSlot.Sc);
        var finalTc = normalized.FirstOrDefault(item => item.Slot == SubtitleSlot.Tc)?.Candidate.Item.Path ?? PathFor(SubtitleSlot.Tc);
        if (InputRules.SubtitlePathsConflict(finalSc, finalTc, finalSc == finalTc)) return new(false, DraftCodes.Duplicate);
        return new(true, null, new DraftAcceptancePlan(this, DraftOperation.Subtitles, revision, [], normalized, requiresConfirmation), requiresConfirmation);
    }

    private static string? ValidateFact(InputPathFacts fact, bool expectsDirectory, bool isVideo)
    {
        var kindError = InputRules.CheckCandidateKind(fact.Kind, expectsDirectory);
        if (kindError == InputKindError.NonLocal) return DraftCodes.NonLocal;
        if (kindError == InputKindError.WrongType) return DraftCodes.WrongType;
        if (!InputRules.AcceptsFixedSlotPath(fact.FullPath, fact.IsFullyQualified)) return DraftCodes.InvalidPath;
        if (!InputRules.AcceptsNormalizedPathSyntax(fact.FullPath, fact.ContainsInvalidPathChars))
            return DraftCodes.InvalidPath;
        if (expectsDirectory ? !fact.DirectoryExists : !fact.FileExists) return DraftCodes.MissingPath;
        if (!expectsDirectory && !InputRules.AcceptsFixedSlotExtension(fact.Extension, isVideo)) return DraftCodes.WrongType;
        return null;
    }
}
