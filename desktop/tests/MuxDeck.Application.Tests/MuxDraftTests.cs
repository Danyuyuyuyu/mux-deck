using MuxDeck.Application.Drafts;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

// 4b-1 草稿会话模型：修订号、事件、编辑锁、准备→提交与 ToSpec 快照。
public sealed class MuxDraftTests
{
    private static readonly ToolConfiguration Tools = new("tools", "logs", "cache");

    // ---------- 修订号与事件 ----------

    [Fact]
    public void Same_value_assignment_does_not_increase_revision_or_raise_event()
    {
        var draft = new MuxDraft(Facts());
        draft.SetVideoPath("C:/in/v.mkv");
        draft.SetFontsDirectory("C:/fonts");
        draft.SetOutputDirectory("C:/out");
        draft.SetOutputFileName("out.mkv");
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        var revision = draft.DraftRevision;
        var raised = 0;
        draft.Changed += (_, _) => raised++;

        draft.SetVideoPath("C:/in/v.mkv");
        draft.SetFontsDirectory("C:/fonts");
        draft.SetOutputDirectory("C:/out");
        draft.SetOutputFileName("out.mkv");
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        draft.SetSubtitleIncluded(SubtitleSlot.Sc, true);
        draft.SetSubtitleName(SubtitleSlot.Sc, "简体");

        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void One_batch_increases_revision_once_and_nested_batches_still_count_once()
    {
        var draft = new MuxDraft(Facts());
        var raised = new List<long>();
        draft.Changed += (_, value) => raised.Add(value);
        var revision = draft.DraftRevision;

        draft.Batch(() =>
        {
            draft.SetVideoPath("C:/in/v.mkv");
            draft.SetFontsDirectory("C:/fonts");
            draft.Batch(() =>
            {
                draft.SetOutputDirectory("C:/out");
                draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
            });
            draft.SetOutputFileName("out.mkv");
        });

        Assert.Equal(revision + 1, draft.DraftRevision);
        Assert.Equal([revision + 1], raised);
    }

    [Fact]
    public void Batch_without_real_change_does_not_increase_revision()
    {
        var draft = new MuxDraft(Facts());
        draft.SetVideoPath("C:/in/v.mkv");
        var revision = draft.DraftRevision;
        var raised = 0;
        draft.Changed += (_, _) => raised++;

        draft.Batch(() =>
        {
            draft.SetVideoPath("C:/in/v.mkv");
            draft.SetOutputFileName("");
        });

        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Every_document_field_increases_revision_once()
    {
        var draft = new MuxDraft(Facts());
        var steps = new (string Name, Action<MuxDraft> Change)[]
        {
            ("视频", value => Assert.True(value.SetVideoPath("C:/in/v.mkv").Succeeded)),
            ("字幕路径", value => Assert.True(value.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass").Succeeded)),
            ("槽位纳入", value => Assert.True(value.SetSubtitleIncluded(SubtitleSlot.Sc, false).Succeeded)),
            ("槽位名称", value => Assert.True(value.SetSubtitleName(SubtitleSlot.Sc, "SC 更名").Succeeded)),
            ("槽位默认", value => Assert.True(value.SetSubtitleDefault(SubtitleSlot.Sc, false).Succeeded)),
            ("槽位强制", value => Assert.True(value.SetSubtitleForced(SubtitleSlot.Sc, true).Succeeded)),
            ("字体目录", value => Assert.True(value.SetFontsDirectory("C:/fonts").Succeeded)),
            ("输出目录", value => Assert.True(value.SetOutputDirectory("C:/out").Succeeded)),
            ("输出文件名", value => Assert.True(value.SetOutputFileName("out.mkv").Succeeded))
        };
        foreach (var step in steps)
        {
            var revision = draft.DraftRevision;
            step.Change(draft);
            Assert.Equal(revision + 1, draft.DraftRevision);
        }
    }

    // ---------- 默认规则与槽位 ----------

    [Fact]
    public void Empty_draft_keeps_sc_default_enabled()
    {
        var draft = new MuxDraft(Facts());
        Assert.True(draft.Sc.IsDefault);
        Assert.False(draft.Tc.IsDefault);
        Assert.True(TrackPolicy.InitialScDefault);
    }

    [Fact]
    public void Subtitle_defaults_follow_domain_policy_when_slots_change()
    {
        var draft = new MuxDraft(Facts());
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        Assert.True(draft.Sc.IsDefault);
        Assert.False(draft.Tc.IsDefault);
        draft.SetSubtitlePath(SubtitleSlot.Tc, "C:/in/tc.ass");
        Assert.True(draft.Sc.IsDefault);
        Assert.False(draft.Tc.IsDefault);
        draft.SetSubtitlePath(SubtitleSlot.Sc, "");
        Assert.False(draft.Sc.IsDefault);
        Assert.True(draft.Tc.IsDefault);
        // 两个槽位都为空时按 Domain 规则得到零默认轨（空草稿的初始 SC 默认只在首次变化前生效）。
        draft.SetSubtitlePath(SubtitleSlot.Tc, "");
        Assert.False(draft.Sc.IsDefault);
        Assert.False(draft.Tc.IsDefault);
    }

    [Fact]
    public void Slot_swap_in_one_batch_increases_revision_once()
    {
        var draft = new MuxDraft(Facts());
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        draft.SetSubtitlePath(SubtitleSlot.Tc, "C:/in/tc.ass");
        var revision = draft.DraftRevision;
        draft.Batch(() =>
        {
            draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/tc.ass");
            draft.SetSubtitlePath(SubtitleSlot.Tc, "C:/in/sc.ass");
        });
        Assert.Equal(revision + 1, draft.DraftRevision);
        Assert.Equal("C:/in/tc.ass", draft.Sc.Path);
        Assert.Equal("C:/in/sc.ass", draft.Tc.Path);
    }

    // ---------- 编辑锁 ----------

    [Fact]
    public void Locked_draft_refuses_every_change_without_throwing_or_touching_revision()
    {
        var draft = new MuxDraft(Facts());
        draft.SetVideoPath("C:/in/v.mkv");
        var before = draft.Snapshot();
        draft.SetLocked(true);
        var revision = draft.DraftRevision;
        var raised = 0;
        draft.Changed += (_, _) => raised++;

        Assert.Equal(DraftCodes.Locked, draft.SetVideoPath("C:/in/other.mkv").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetSubtitleIncluded(SubtitleSlot.Sc, false).ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetSubtitleName(SubtitleSlot.Sc, "更名").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetFontsDirectory("C:/fonts").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetOutputDirectory("C:/out").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.SetOutputFileName("out.mkv").ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.AddExternalTrack(Audio("a1", "C:/in/a.mka")).ErrorCode);
        Assert.Equal(DraftCodes.Locked, draft.RemoveExternalTrack("a1").ErrorCode);

        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(0, raised);
        Assert.Equal(before, draft.Snapshot());
        Assert.False(draft.CanEdit);
    }

    [Fact]
    public void Lock_change_does_not_touch_draft_content()
    {
        var draft = new MuxDraft(Facts());
        var revision = draft.DraftRevision;
        var editability = 0;
        draft.EditabilityChanged += (_, _) => editability++;
        draft.SetLocked(true);
        draft.SetLocked(true);
        draft.SetLocked(false);
        Assert.Equal(2, editability);
        Assert.Equal(revision, draft.DraftRevision);
        Assert.True(draft.CanEdit);
    }

    // ---------- 准备 → 提交 ----------

    [Fact]
    public async Task Prepare_then_commit_applies_a_batch_and_increases_revision_once()
    {
        var draft = new MuxDraft(Facts());
        var prepared = await draft.PrepareAsync(DraftOperation.Subtitles,
            [], [new(SubtitleSlot.Sc, Candidate("C:/in/sc.ass")), new(SubtitleSlot.Tc, Candidate("C:/in/tc.ass"))],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(prepared.Succeeded);
        Assert.False(prepared.RequiresConfirmation);
        Assert.Equal(0, draft.DraftRevision);
        Assert.Equal("", draft.Sc.Path);

        var revision = draft.DraftRevision;
        Assert.Equal(revision, prepared.Plan!.Revision);
        var committed = draft.Commit(prepared.Plan);
        Assert.True(committed.Succeeded);
        Assert.Equal(revision + 1, draft.DraftRevision);
        Assert.Equal("C:/in/sc.ass", draft.Sc.Path);
        Assert.Equal("C:/in/tc.ass", draft.Tc.Path);
    }

    [Fact]
    public async Task Plan_is_stale_after_the_draft_changed()
    {
        var draft = new MuxDraft(Facts());
        var prepared = await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/v.mkv")], [], cancellationToken: TestContext.Current.CancellationToken);
        draft.SetOutputFileName("changed.mkv");
        var before = draft.Snapshot();
        var committed = draft.Commit(prepared.Plan!);
        Assert.Equal(DraftCodes.Stale, committed.ErrorCode);
        Assert.Equal(before, draft.Snapshot());
    }

    [Fact]
    public async Task Plan_from_another_draft_is_stale()
    {
        var first = new MuxDraft(Facts());
        var second = new MuxDraft(Facts());
        var prepared = await first.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/v.mkv")], [], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(DraftCodes.Stale, second.Commit(prepared.Plan!).ErrorCode);
    }

    [Fact]
    public async Task Replacement_requires_confirmation_before_it_is_applied()
    {
        var draft = new MuxDraft(Facts());
        draft.SetVideoPath("C:/in/v.mkv");
        var prepared = await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/other.mkv")], [], cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(prepared.RequiresConfirmation);
        Assert.True(prepared.Plan!.RequiresReplacementConfirmation);
        Assert.Equal(DraftCodes.ConfirmationRequired, draft.Commit(prepared.Plan).ErrorCode);
        Assert.Equal("C:/in/v.mkv", draft.VideoPath);
        Assert.True(draft.Commit(prepared.Plan, replacementsConfirmed: true).Succeeded);
        Assert.Equal("C:/in/other.mkv", draft.VideoPath);
    }

    [Fact]
    public async Task Commit_rechecks_the_edit_lock()
    {
        var draft = new MuxDraft(Facts());
        var prepared = await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/v.mkv")], [], cancellationToken: TestContext.Current.CancellationToken);
        draft.SetLocked(true);
        var before = draft.Snapshot();
        Assert.Equal(DraftCodes.Locked, draft.Commit(prepared.Plan!).ErrorCode);
        Assert.Equal(before, draft.Snapshot());
    }

    [Fact]
    public async Task Prepare_rechecks_path_facts_and_reports_codes_without_text()
    {
        var draft = new MuxDraft(Facts());
        Assert.Equal(DraftCodes.MissingPath,
            (await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/missing.mkv")], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.WrongType,
            (await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/note.txt")], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.InvalidCount,
            (await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/v.mkv"), Candidate("C:/in/v2.mkv")], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.NonLocal,
            (await draft.PrepareAsync(DraftOperation.Video, [Candidate("http://example.test/v.mkv", InputLocationKind.NonLocal)], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.InvalidPath,
            (await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/bad.mkv")], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.Duplicate,
            (await draft.PrepareAsync(DraftOperation.Subtitles, [],
                [new(SubtitleSlot.Sc, Candidate("C:/in/sc.ass")), new(SubtitleSlot.Tc, Candidate("C:/in/sc.ass"))],
                cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot,
            (await draft.PrepareAsync(DraftOperation.Subtitles, [],
                [new(SubtitleSlot.Tc, Candidate("C:/in/sc.ass")), new(SubtitleSlot.Tc, Candidate("C:/in/tc.ass"))],
                cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(DraftCodes.Locked, (await Locked().PrepareAsync(DraftOperation.Video, [Candidate("C:/in/v.mkv")], [], cancellationToken: TestContext.Current.CancellationToken)).ErrorCode);
    }

    // ---------- ToSpec ----------

    [Fact]
    public async Task ToSpec_snapshot_is_isolated_from_later_draft_changes()
    {
        var draft = new MuxDraft(Facts());
        draft.SetVideoPath("C:/in/v.mkv");
        draft.SetFontsDirectory("C:/fonts");
        draft.SetOutputDirectory("C:/out");
        var prepared = await draft.PrepareAsync(DraftOperation.Subtitles, [], [new(SubtitleSlot.Sc, Candidate("C:/in/sc.ass"))], cancellationToken: TestContext.Current.CancellationToken);
        draft.Commit(prepared.Plan!);
        var spec = draft.ToSpec(Tools);

        draft.SetOutputFileName("later.mkv");
        draft.SetSubtitleName(SubtitleSlot.Sc, "更名");
        draft.SetSubtitleIncluded(SubtitleSlot.Sc, false);
        draft.SetVideoPath("C:/in/other.mkv");
        draft.AddExternalTrack(Audio("a1", "C:/in/a.mka"));

        Assert.Equal("C:/in/v.mkv", spec.VideoPath);
        Assert.Equal("C:/in/sc.ass", spec.ExternalTracks[0].Path);
        Assert.Equal("简体", spec.ExternalTracks[0].Name);
        Assert.Null(spec.Output.FileName);
        Assert.Single(spec.ExternalTracks);
    }

    // 与 4b-1 特征测试冻结的 8 种草稿状态逐一相等：只换构造入口，规格必须完全一致。
    [Fact]
    public async Task ToSpec_matches_the_frozen_vm_specs_for_every_draft_state()
    {
        // 仅 SC
        var scOnly = new MuxDraft(Facts());
        SetVideoAndFonts(scOnly);
        await Accept(scOnly, SubtitleSlot.Sc, "C:/in/sc.ass");
        AssertSpec(scOnly.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/sc.ass", TrackKind.Subtitle, null, "简体", new("zh-Hans"), new(true, false))],
            "C:/fonts", new("C:/out", null));

        // 仅 TC
        var tcOnly = new MuxDraft(Facts());
        SetVideoAndFonts(tcOnly);
        await Accept(tcOnly, SubtitleSlot.Tc, "C:/in/tc.ass");
        AssertSpec(tcOnly.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/tc.ass", TrackKind.Subtitle, null, "繁体", new("zh-Hant"), new(true, false))],
            "C:/fonts", new("C:/out", null));

        // SC + TC
        var both = new MuxDraft(Facts());
        SetVideoAndFonts(both);
        await Accept(both, SubtitleSlot.Sc, "C:/in/sc.ass");
        await Accept(both, SubtitleSlot.Tc, "C:/in/tc.ass");
        AssertSpec(both.ToSpec(Tools), "C:/in/v.mkv", [
            new("C:/in/sc.ass", TrackKind.Subtitle, null, "简体", new("zh-Hans"), new(true, false)),
            new("C:/in/tc.ass", TrackKind.Subtitle, null, "繁体", new("zh-Hant"), new(false, false))],
            "C:/fonts", new("C:/out", null));

        // TC 被排除
        var excluded = new MuxDraft(Facts());
        SetVideoAndFonts(excluded);
        await Accept(excluded, SubtitleSlot.Sc, "C:/in/sc.ass");
        await Accept(excluded, SubtitleSlot.Tc, "C:/in/tc.ass");
        excluded.SetSubtitleIncluded(SubtitleSlot.Tc, false);
        AssertSpec(excluded.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/sc.ass", TrackKind.Subtitle, null, "简体", new("zh-Hans"), new(true, false))],
            "C:/fonts", new("C:/out", null));

        // 外加音频
        var audioDraft = new MuxDraft(Facts());
        SetVideoAndFonts(audioDraft);
        audioDraft.AddExternalTrack(Audio("a2", "C:/in/audio.wav", 2, included: true));
        AssertSpec(audioDraft.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/audio.wav", TrackKind.Audio, new(2), "", new("und"), new(false, false))],
            "C:/fonts", new("C:/out", null));

        // 多音轨同容器
        var multiAudio = new MuxDraft(Facts());
        SetVideoAndFonts(multiAudio);
        multiAudio.AddExternalTrack(Audio("a2", "C:/in/multi.wav", 2, included: true));
        AssertSpec(multiAudio.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/multi.wav", TrackKind.Audio, new(2), "", new("und"), new(false, false))],
            "C:/fonts", new("C:/out", null));

        // 其他字幕
        var other = new MuxDraft(Facts());
        SetVideoAndFonts(other);
        await Accept(other, SubtitleSlot.Sc, "C:/in/sc.ass");
        other.AddExternalTrack(OtherSubtitle("s1", "C:/in/other.ass"));
        AssertSpec(other.ToSpec(Tools), "C:/in/v.mkv", [
            new("C:/in/sc.ass", TrackKind.Subtitle, null, "简体", new("zh-Hans"), new(true, false)),
            new("C:/in/other.ass", TrackKind.Subtitle, null, "", new("und"), new(false, false))],
            "C:/fonts", new("C:/out", null));

        // 自定义文件名
        var custom = new MuxDraft(Facts());
        SetVideoAndFonts(custom);
        await Accept(custom, SubtitleSlot.Sc, "C:/in/sc.ass");
        custom.SetOutputFileName("自定义 成品.mkv");
        AssertSpec(custom.ToSpec(Tools), "C:/in/v.mkv",
            [new("C:/in/sc.ass", TrackKind.Subtitle, null, "简体", new("zh-Hans"), new(true, false))],
            "C:/fonts", new("C:/out", "自定义 成品.mkv"));
    }

    // ---------- 辅助 ----------

    private static MuxDraft Locked()
    {
        var draft = new MuxDraft(Facts());
        draft.SetLocked(true);
        return draft;
    }

    private static void SetVideoAndFonts(MuxDraft draft)
    {
        Assert.True(draft.SetVideoPath("C:/in/v.mkv").Succeeded);
        Assert.True(draft.SetFontsDirectory("C:/fonts").Succeeded);
        Assert.True(draft.SetOutputDirectory("C:/out").Succeeded);
    }

    private static async Task Accept(MuxDraft draft, SubtitleSlot slot, string path)
    {
        var prepared = await draft.PrepareAsync(DraftOperation.Subtitles, [], [new(slot, Candidate(path))], cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(prepared.Succeeded);
        Assert.True(draft.Commit(prepared.Plan!, replacementsConfirmed: true).Succeeded);
    }

    private static void AssertSpec(MuxJobSpec spec, string video, ExternalTrackSpec[] tracks, string fonts, OutputSpec output)
    {
        Assert.Equal(video, spec.VideoPath);
        Assert.Equal(tracks, spec.ExternalTracks.ToArray());
        // 草稿没有可编辑的源轨选择，空数组与旧入口一致。
        Assert.Empty(spec.SourceTracks.ToArray());
        Assert.Equal(new FontSpec(fonts), spec.Fonts);
        Assert.Equal(output, spec.Output);
        Assert.Equal(Tools, spec.Tools);
    }

    private static DraftCandidate Candidate(string path, InputLocationKind kind = InputLocationKind.LocalFile) =>
        new(new InputItem(path, kind));

    private static DraftExternalTrackSnapshot Audio(string identity, string path, int? trackId = null, bool included = true) =>
        new(identity, path, TrackKind.Audio, trackId is { } id ? new TrackId(id) : null, "", new LanguageTag("und"),
            new TrackFlags(false, false)) { IsIncluded = included };

    private static DraftExternalTrackSnapshot OtherSubtitle(string identity, string path) =>
        new(identity, path, TrackKind.Subtitle, null, "", new LanguageTag("und"), new TrackFlags(false, false));

    private sealed class FakePathFacts : IPathFacts
    {
        public Dictionary<string, InputPathFacts> Items { get; } = new(StringComparer.Ordinal);
        public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EquatableArray<InputPathFacts>([.. items.Select(Item)]));

        private InputPathFacts Item(InputItem item)
        {
            if (Items.TryGetValue(item.Path, out var known)) return known;
            // 未登记的路径按“不存在”处理，缺文件路径必须被拒绝。
            return new(item.Path, true, item.Path, Path.GetExtension(item.Path), "",
                item.Kind, FileExists: false, DirectoryExists: item.Kind == InputLocationKind.LocalDirectory);
        }
    }

    private static FakePathFacts Facts()
    {
        var facts = new FakePathFacts();
        facts.Items["C:/in/v.mkv"] = new("C:/in/v.mkv", true, "C:/in/v.mkv", ".mkv", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/other.mkv"] = new("C:/in/other.mkv", true, "C:/in/other.mkv", ".mkv", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/v2.mkv"] = new("C:/in/v2.mkv", true, "C:/in/v2.mkv", ".mkv", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/missing.mkv"] = new("C:/in/missing.mkv", true, "C:/in/missing.mkv", ".mkv", "C:/in", InputLocationKind.LocalFile, false, false);
        // 数据流标记等非法路径字符由端口事实报告，Application 不调用 System.IO。
        facts.Items["C:/in/bad.mkv"] = new("C:/in/bad.mkv", true, "C:/in/bad.mkv", ".mkv", "C:/in", InputLocationKind.LocalFile, true, false, ContainsInvalidPathChars: true);
        facts.Items["C:/in/note.txt"] = new("C:/in/note.txt", true, "C:/in/note.txt", ".txt", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/sc.ass"] = new("C:/in/sc.ass", true, "C:/in/sc.ass", ".ass", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/tc.ass"] = new("C:/in/tc.ass", true, "C:/in/tc.ass", ".ass", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/other.ass"] = new("C:/in/other.ass", true, "C:/in/other.ass", ".ass", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/audio.wav"] = new("C:/in/audio.wav", true, "C:/in/audio.wav", ".wav", "C:/in", InputLocationKind.LocalFile, true, false);
        facts.Items["C:/in/multi.wav"] = new("C:/in/multi.wav", true, "C:/in/multi.wav", ".wav", "C:/in", InputLocationKind.LocalFile, true, false);
        return facts;
    }
}