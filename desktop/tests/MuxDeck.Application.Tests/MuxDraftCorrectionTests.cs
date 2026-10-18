using MuxDeck.Application.Drafts;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

// 4b-2 草稿补齐：同步入口、原始路径判定、结构化原因、槽位顺序、ToSpec 顺序、
// 导入冲突/默认旗标与整批原子性。
public sealed class MuxDraftCorrectionTests
{
    private static readonly ToolConfiguration Tools = new("tools", "logs", "cache");

    // ---------- 1.1 同步入口与同一校验核心 ----------

    [Fact]
    public void Sync_prepare_and_commit_use_the_sync_facts_without_an_async_port()
    {
        var facts = new FakeFacts();
        facts.Add(Fact("C:/in/v.mkv", ".mkv"));
        var draft = new MuxDraft(facts);

        var prepared = draft.Prepare(DraftOperation.Video, [Candidate("C:/in/v.mkv")], []);
        Assert.True(prepared.Succeeded);
        Assert.True(draft.Commit(prepared.Plan!).Succeeded);
        Assert.Equal("C:/in/v.mkv", draft.VideoPath);
        Assert.Equal(1, draft.DraftRevision);
    }

    [Fact]
    public async Task Sync_and_async_entries_report_the_same_rejection()
    {
        var facts = new FakeFacts();
        facts.Add(Fact("C:/in/missing.mkv", ".mkv", exists: false));
        var draft = new MuxDraft(facts);

        var sync = draft.Prepare(DraftOperation.Video, [Candidate("C:/in/missing.mkv")], []);
        var asyncResult = await draft.PrepareAsync(DraftOperation.Video, [Candidate("C:/in/missing.mkv")], [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(asyncResult.ErrorCode, sync.ErrorCode);
        Assert.Equal(asyncResult.Reason, sync.Reason);
        Assert.Equal(DraftCodes.MissingPath, sync.ErrorCode);
        Assert.Equal(DraftReason.FileMissing, sync.Reason);
    }

    // ---------- 1.2 原始路径判定 ----------

    [Fact]
    public void Raw_path_with_url_separator_is_rejected_even_when_normalized_path_looks_valid()
    {
        var facts = new FakeFacts();
        facts.Add(new("C:\\a://b.ass", true, "C:\\a\\b.ass", ".ass", "C:\\a",
            InputLocationKind.LocalFile, true, false, false, null));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.Subtitles, [],
            [new(SubtitleSlot.Sc, Candidate("C:\\a://b.ass"))]);

        Assert.Equal(DraftCodes.InvalidPath, result.ErrorCode);
        Assert.Equal(DraftReason.PathNotAbsolute, result.Reason);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void Forward_slash_device_prefix_is_rejected_by_normalized_syntax()
    {
        var facts = new FakeFacts();
        facts.Add(new("//?/C:/a.ass", true, "\\\\?\\C:\\a.ass", ".ass", "\\\\?\\C:\\",
            InputLocationKind.LocalFile, true, false, false, null));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.Subtitles, [],
            [new(SubtitleSlot.Sc, Candidate("//?/C:/a.ass"))]);

        Assert.Equal(DraftCodes.InvalidPath, result.ErrorCode);
        Assert.Equal(DraftReason.InvalidPathSyntax, result.Reason);
    }

    // ---------- 1.4 结构化原因 ----------

    [Theory]
    [InlineData(InputLocationKind.NonLocal, "C:/in/a.ass", true, true, false, ".ass", DraftCodes.NonLocal, DraftReason.NonLocal)]
    [InlineData(InputLocationKind.LocalDirectory, "C:/in/a.ass", true, true, true, ".ass", DraftCodes.WrongType, DraftReason.WrongLocationKindFile)]
    [InlineData(InputLocationKind.LocalFile, "a.ass", false, true, true, ".ass", DraftCodes.InvalidPath, DraftReason.PathNotAbsolute)]
    [InlineData(InputLocationKind.LocalFile, "C:/in/a.ass", true, true, true, ".ass", DraftCodes.InvalidPath, DraftReason.InvalidPathSyntax)]
    [InlineData(InputLocationKind.LocalFile, "C:/in/a.ass", true, false, false, ".ass", DraftCodes.MissingPath, DraftReason.FileMissing)]
    [InlineData(InputLocationKind.LocalFile, "C:/in/a.ass", true, false, true, ".txt", DraftCodes.WrongType, DraftReason.UnsupportedSubtitleExtension)]
    public void Fact_rejections_expose_structured_reasons(InputLocationKind kind, string path, bool fullyQualified,
        bool invalidChars, bool exists, string extension, string expectedCode, DraftReason expectedReason)
    {
        var facts = new FakeFacts();
        facts.Add(new(path, fullyQualified, path, extension, "C:\\in", kind, exists, false, invalidChars, null));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.Subtitles, [], [new(SubtitleSlot.Sc, Candidate(path, kind))]);

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void Inaccessible_path_keeps_detail_and_reason()
    {
        var facts = new FakeFacts();
        facts.Add(new("C:\\bad\0.ass", true, "C:\\bad\0.ass", "", "", InputLocationKind.LocalFile,
            false, false, false, "路径无效或不可访问：bad"));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.Subtitles, [],
            [new(SubtitleSlot.Sc, Candidate("C:\\bad\0.ass"))]);

        Assert.Equal(DraftCodes.InvalidPath, result.ErrorCode);
        Assert.Equal(DraftReason.PathInaccessible, result.Reason);
        Assert.Equal("路径无效或不可访问：bad", result.Detail);
    }

    [Theory]
    [InlineData(InputLocationKind.LocalFile, DraftReason.WrongLocationKindDirectory)]
    public void Directory_operations_report_directory_kind_reason(InputLocationKind kind, DraftReason expected)
    {
        var facts = new FakeFacts();
        facts.Add(new("C:/fonts", true, "C:/fonts", "", "C:/fonts", kind, false, true, false, null));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.FontsDirectory,
            [Candidate("C:/fonts", kind)], []);

        Assert.Equal(DraftCodes.WrongType, result.ErrorCode);
        Assert.Equal(expected, result.Reason);
    }

    [Fact]
    public void Missing_directory_reason_is_distinct()
    {
        var facts = new FakeFacts();
        facts.Add(new("C:/fonts", true, "C:/fonts", "", "C:/fonts", InputLocationKind.LocalDirectory, false, false, false, null));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.FontsDirectory,
            [Candidate("C:/fonts", InputLocationKind.LocalDirectory)], []);

        Assert.Equal(DraftCodes.MissingPath, result.ErrorCode);
        Assert.Equal(DraftReason.DirectoryMissing, result.Reason);
    }

    [Fact]
    public void Unsupported_video_extension_reason_is_distinct()
    {
        var facts = new FakeFacts();
        facts.Add(Fact("C:/in/v.avi", ".avi"));
        var draft = new MuxDraft(facts);

        var result = draft.Prepare(DraftOperation.Video, [Candidate("C:/in/v.avi")], []);

        Assert.Equal(DraftCodes.WrongType, result.ErrorCode);
        Assert.Equal(DraftReason.UnsupportedVideoExtension, result.Reason);
    }

    // ---------- 1.5 无效槽位 ----------

    [Fact]
    public void Unknown_slot_values_return_invalid_slot_without_throwing()
    {
        var draft = new MuxDraft(new FakeFacts());
        var invalid = (SubtitleSlot)99;

        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitlePath(invalid, "C:/in/a.ass").ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleIncluded(invalid, true).ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleName(invalid, "x").ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleDefault(invalid, true).ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleForced(invalid, true).ErrorCode);
        Assert.Equal("", draft.PathFor(invalid));
        Assert.False(draft.IsSlotIncluded(invalid));
        Assert.Equal("", draft.Slot(invalid).Path);
        Assert.Equal(0, draft.DraftRevision);
    }

    [Fact]
    public void Slot_check_precedes_missing_path_and_lock_precedes_slot()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.Equal(DraftCodes.MissingPath, draft.SetSubtitleIncluded(SubtitleSlot.Sc, true).ErrorCode);
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleIncluded((SubtitleSlot)99, true).ErrorCode);

        draft.SetLocked(true);
        Assert.Equal(DraftCodes.Locked, draft.SetSubtitleIncluded((SubtitleSlot)99, true).ErrorCode);
    }

    // ---------- 1.6 ToSpec 顺序 ----------

    [Fact]
    public void ToSpec_orders_subtitles_before_audio_and_clears_external_subtitle_source_id()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("audio", "C:/in/a.mka", TrackKind.Audio, 7, "音频")).Succeeded);
        Assert.True(draft.AddExternalTrack(Track("sub", "C:/in/other.ass", TrackKind.Subtitle, 1, "字幕")).Succeeded);
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");

        var spec = draft.ToSpec(Tools);

        Assert.Equal(["C:/in/sc.ass", "C:/in/other.ass", "C:/in/a.mka"],
            spec.ExternalTracks.Select(track => track.Path).ToArray());
        Assert.Null(spec.ExternalTracks[1].SourceTrackId);
        Assert.Equal(new TrackId(7), spec.ExternalTracks[2].SourceTrackId);
    }

    [Fact]
    public void Conflict_snapshot_includes_excluded_tracks_and_slots()
    {
        var draft = new MuxDraft(new FakeFacts());
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        draft.SetSubtitleIncluded(SubtitleSlot.Sc, false);
        Assert.True(draft.AddExternalTrack(Track("sub", "C:/in/other.ass", TrackKind.Subtitle, 1, "字幕")).Succeeded);
        draft.SetExternalIncluded("sub", false);

        var conflict = draft.ToConflictSpec(Tools);
        var active = draft.ToSpec(Tools);

        Assert.Equal(2, conflict.ExternalTracks.Count);
        Assert.Empty(active.ExternalTracks);
    }

    // ---------- 1.7 导入冲突 ----------

    [Fact]
    public void Different_path_with_the_same_track_id_is_not_a_conflict()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("a1", "C:/in/a.mka", TrackKind.Audio, 7, "音频")).Succeeded);
        Assert.True(draft.AddExternalTrack(Track("a2", "C:/in/other.mka", TrackKind.Audio, 7, "音频")).Succeeded);
        Assert.Equal(2, draft.ExternalTracks.Count);
    }

    [Fact]
    public void Same_path_and_track_id_conflicts_across_identities()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("a1", "C:/in/a.mka", TrackKind.Audio, 7, "音频")).Succeeded);
        Assert.Equal(DraftCodes.Duplicate,
            draft.AddExternalTrack(Track("a2", "C:/in/a.mka", TrackKind.Audio, 7, "音频")).ErrorCode);
    }

    [Fact]
    public void Subtitle_with_the_same_path_conflicts_regardless_of_track_id()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("s1", "C:/in/other.ass", TrackKind.Subtitle, 1, "字幕")).Succeeded);
        Assert.Equal(DraftCodes.Duplicate,
            draft.AddExternalTrack(Track("s2", "C:/in/other.ass", TrackKind.Subtitle, 2, "字幕")).ErrorCode);
    }

    // ---------- 1.8 导入默认旗标 ----------

    [Fact]
    public void Import_defaults_only_the_first_subtitle_in_a_batch()
    {
        var draft = new MuxDraft(new FakeFacts());

        Assert.True(draft.CommitImport(new(null, null,
        [
            Import("s1", "C:/in/one.ass", TrackKind.Subtitle, 1, hasDefaultFlag: false, isDefault: false),
            Import("s2", "C:/in/two.ass", TrackKind.Subtitle, 2, hasDefaultFlag: false, isDefault: false)
        ])).Succeeded);

        Assert.True(draft.ExternalTracks[0].Flags.IsDefault);
        Assert.False(draft.ExternalTracks[1].Flags.IsDefault);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Import_audio_default_requires_source_flag_and_default(bool hasFlag, bool isDefault, bool expected)
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.CommitImport(new(null, null,
            [Import("a1", "C:/in/a.mka", TrackKind.Audio, 7, hasFlag, isDefault)])).Succeeded);
        Assert.Equal(expected, draft.ExternalTracks[0].Flags.IsDefault);
    }

    [Fact]
    public void Excluded_slot_still_suppresses_imported_subtitle_default()
    {
        var draft = new MuxDraft(new FakeFacts());
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        draft.SetSubtitleIncluded(SubtitleSlot.Sc, false);

        Assert.True(draft.CommitImport(new(null, null,
            [Import("s1", "C:/in/one.ass", TrackKind.Subtitle, 1, false, false)])).Succeeded);

        Assert.False(draft.ExternalTracks[0].Flags.IsDefault);
    }

    [Fact]
    public void Excluded_external_subtitle_still_suppresses_imported_subtitle_default()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("old", "C:/in/old.ass", TrackKind.Subtitle, 1, "字幕")).Succeeded);
        draft.SetExternalIncluded("old", false);

        Assert.True(draft.CommitImport(new(null, null,
            [Import("new", "C:/in/new.ass", TrackKind.Subtitle, 2, false, false)])).Succeeded);

        Assert.False(draft.ExternalTracks[1].Flags.IsDefault);
    }

    // ---------- 1.9 整批原子性 ----------

    [Fact]
    public void Rejected_import_leaves_draft_and_revision_untouched()
    {
        var facts = new FakeFacts();
        facts.Add(Fact("C:/new/v.mkv", ".mkv"));
        var draft = new MuxDraft(facts);
        Assert.True(draft.AddExternalTrack(Track("old", "C:/in/a.mka", TrackKind.Audio, 7, "音频")).Succeeded);
        var before = draft.Snapshot();
        var revision = draft.DraftRevision;

        var result = draft.CommitImport(new("C:/new/v.mkv", "C:/new/fonts",
        [
            Import("a2", "C:/in/b.mka", TrackKind.Audio, 8, false, false),
            Import("a3", "C:/in/a.mka", TrackKind.Audio, 7, false, false)
        ]));

        Assert.Equal(DraftCodes.Duplicate, result.ErrorCode);
        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(before, draft.Snapshot());
    }

    [Fact]
    public void Import_requires_confirmation_for_video_replacement_and_applies_one_revision()
    {
        var facts = new FakeFacts();
        facts.Add(Fact("C:/old/v.mkv", ".mkv"));
        facts.Add(Fact("C:/new/v.mkv", ".mkv"));
        var draft = new MuxDraft(facts);
        draft.SetVideoPath("C:/old/v.mkv");
        var revision = draft.DraftRevision;

        var refused = draft.CommitImport(new("C:/new/v.mkv", null,
            [Import("a1", "C:/in/a.mka", TrackKind.Audio, 7, false, false)]));
        Assert.Equal(DraftCodes.ConfirmationRequired, refused.ErrorCode);
        Assert.Equal(revision, draft.DraftRevision);

        Assert.True(draft.CommitImport(new("C:/new/v.mkv", "C:/fonts",
            [Import("a1", "C:/in/a.mka", TrackKind.Audio, 7, false, false)], VideoReplacementConfirmed: true)).Succeeded);
        Assert.Equal(revision + 1, draft.DraftRevision);
        Assert.Equal("C:/new/v.mkv", draft.VideoPath);
        Assert.Equal("C:/fonts", draft.FontsDirectory);
        Assert.Single(draft.ExternalTracks);
    }

    // ---------- ReplaceExternalTrack ----------

    [Fact]
    public void Replace_external_track_updates_path_and_source_id_and_keeps_identity_metadata()
    {
        var draft = new MuxDraft(new FakeFacts());
        var original = new DraftExternalTrackSnapshot("a1", "C:/in/a.mka", TrackKind.Audio, new TrackId(2),
            "原名", new LanguageTag("eng"), new TrackFlags(true, true)) { IsIncluded = false };
        Assert.True(draft.AddExternalTrack(original).Succeeded);
        var revision = draft.DraftRevision;
        var changed = 0;
        draft.Changed += (_, _) => changed++;

        Assert.True(draft.ReplaceExternalTrack("a1",
            new DraftExternalTrackSnapshot("ignored", "C:/in/b.mka", TrackKind.Audio, new TrackId(7),
                "新名", new LanguageTag("jpn"), new TrackFlags(false, false))).Succeeded);

        var track = Assert.Single(draft.ExternalTracks);
        Assert.Equal("a1", track.Identity);
        Assert.Equal("C:/in/b.mka", track.Path);
        Assert.Equal(new TrackId(7), track.SourceTrackId);
        Assert.Equal("原名", track.Name);
        Assert.Equal(new LanguageTag("eng"), track.Language);
        Assert.Equal(new TrackFlags(true, true), track.Flags);
        Assert.False(track.IsIncluded);
        Assert.Equal(revision + 1, draft.DraftRevision);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Replace_external_track_rejects_same_path_and_track_id()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("a1", "C:/in/a.mka", TrackKind.Audio, 2, "音频")).Succeeded);
        Assert.True(draft.AddExternalTrack(Track("a2", "C:/in/b.mka", TrackKind.Audio, 3, "音频")).Succeeded);
        var before = draft.Snapshot();
        var revision = draft.DraftRevision;

        var result = draft.ReplaceExternalTrack("a1",
            Track("ignored", "C:/in/b.mka", TrackKind.Audio, 3, "音频"));

        Assert.Equal(DraftCodes.Duplicate, result.ErrorCode);
        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(before, draft.Snapshot());
    }

    [Fact]
    public void Replace_external_track_conflicts_with_subtitle_slot_path()
    {
        var draft = new MuxDraft(new FakeFacts());
        draft.SetSubtitlePath(SubtitleSlot.Sc, "C:/in/sc.ass");
        Assert.True(draft.AddExternalTrack(Track("a1", "C:/in/a.mka", TrackKind.Audio, 2, "音频")).Succeeded);
        var revision = draft.DraftRevision;

        var result = draft.ReplaceExternalTrack("a1",
            Track("ignored", "C:/in/sc.ass", TrackKind.Audio, 2, "音频"));

        Assert.Equal(DraftCodes.Duplicate, result.ErrorCode);
        Assert.Equal(revision, draft.DraftRevision);
    }

    [Fact]
    public void Replace_external_track_reports_locked_and_missing_identity()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.True(draft.AddExternalTrack(Track("a1", "C:/in/a.mka", TrackKind.Audio, 2, "音频")).Succeeded);
        var replacement = Track("ignored", "C:/in/b.mka", TrackKind.Audio, 3, "音频");

        Assert.Equal(DraftCodes.ExternalTrackMissing, draft.ReplaceExternalTrack("missing", replacement).ErrorCode);
        draft.SetLocked(true);
        Assert.Equal(DraftCodes.Locked, draft.ReplaceExternalTrack("a1", replacement).ErrorCode);
    }

    // ---------- SetSubtitleIncludedRaw ----------

    [Fact]
    public void Raw_inclusion_toggle_matches_the_legacy_empty_slot_behavior()
    {
        // 621ee8b 的 Track_field_change_increments_revision_exactly_once("included")
        // 在空 SC 槽位直接切换行纳入时断言修订号 +1；原始入口不检查缺路径。
        var draft = new MuxDraft(new FakeFacts());
        var revision = draft.DraftRevision;

        Assert.True(draft.SetSubtitleIncludedRaw(SubtitleSlot.Sc, false).Succeeded);
        Assert.False(draft.Sc.IsIncluded);
        Assert.Equal(revision + 1, draft.DraftRevision);

        Assert.True(draft.SetSubtitleIncludedRaw(SubtitleSlot.Sc, false).Succeeded);
        Assert.Equal(revision + 1, draft.DraftRevision);
    }

    [Fact]
    public void Raw_inclusion_toggle_reports_locked_and_unknown_slot()
    {
        var draft = new MuxDraft(new FakeFacts());
        Assert.Equal(DraftCodes.InvalidSlot, draft.SetSubtitleIncludedRaw((SubtitleSlot)99, false).ErrorCode);
        draft.SetLocked(true);
        Assert.Equal(DraftCodes.Locked, draft.SetSubtitleIncludedRaw(SubtitleSlot.Sc, false).ErrorCode);
    }

    // ---------- 辅助 ----------

    private static DraftCandidate Candidate(string path, InputLocationKind kind = InputLocationKind.LocalFile) =>
        new(new InputItem(path, kind));

    private static DraftExternalTrackSnapshot Track(string identity, string path, TrackKind kind, int trackId, string name) =>
        new(identity, path, kind, new TrackId(trackId), name, new LanguageTag("und"), new TrackFlags(false, false));

    private static DraftImportedTrack Import(string identity, string path, TrackKind kind, int trackId,
        bool hasDefaultFlag, bool isDefault) =>
        new(identity, path, kind, new TrackId(trackId), "", new LanguageTag("und"), hasDefaultFlag, isDefault, false);

    private static InputPathFacts Fact(string path, string extension, bool exists = true) =>
        new(path, true, path, extension, "C:/in", InputLocationKind.LocalFile, exists, false, false, null);

    private sealed class FakeFacts : ISlotPathFacts
    {
        private readonly Dictionary<string, InputPathFacts> items = new(StringComparer.Ordinal);

        public void Add(InputPathFacts fact) => items[fact.Path] = fact;

        public IReadOnlyList<InputPathFacts> Collect(IReadOnlyList<InputItem> input,
            CancellationToken cancellationToken = default) => [.. input.Select(Item)];

        public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EquatableArray<InputPathFacts>([.. input.Select(Item)]));

        private InputPathFacts Item(InputItem item) => items.TryGetValue(item.Path, out var known)
            ? known
            : new(item.Path, true, item.Path, Path.GetExtension(item.Path), "", item.Kind, false, false, false, null);
    }
}
