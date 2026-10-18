using MuxDeck.Application.Drafts;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

// 4b-1 第五次修正：路径事实失败只在候选自身校验中上报，顺序与旧 ValidateCandidate 一致。
public sealed class MuxDraftPathFactTests
{
    [Fact]
    public async Task Relative_file_name_is_invalid_path_without_failure_detail()
    {
        var result = await new MuxDraft(SystemPathFacts.Instance).PrepareAsync(DraftOperation.Video,
            [Candidate("a.ass")], [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DraftCodes.InvalidPath, result.ErrorCode);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Non_local_candidate_wins_over_normalization_failure()
    {
        var result = await new MuxDraft(SystemPathFacts.Instance).PrepareAsync(DraftOperation.Video,
            [Candidate("http://example.test/bad\0.mkv", InputLocationKind.NonLocal)], [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DraftCodes.NonLocal, result.ErrorCode);
    }

    [Fact]
    public async Task Duplicate_slot_is_reported_before_the_second_candidate_path_failure()
    {
        var valid = Path.Combine(Path.GetTempPath(), $"muxdeck-{Guid.NewGuid():N}.ass");
        await File.WriteAllTextAsync(valid, "", TestContext.Current.CancellationToken);
        try
        {
            var result = await new MuxDraft(SystemPathFacts.Instance).PrepareAsync(DraftOperation.Subtitles, [],
                [new(SubtitleSlot.Sc, Candidate(valid)),
                 new(SubtitleSlot.Sc, Candidate("C:\\bad\0invalid.ass"))],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(DraftCodes.InvalidSlot, result.ErrorCode);
        }
        finally
        {
            File.Delete(valid);
        }
    }

    [Fact]
    public async Task Fully_qualified_path_that_fails_normalization_keeps_detail()
    {
        var result = await new MuxDraft(SystemPathFacts.Instance).PrepareAsync(DraftOperation.Subtitles, [],
            [new(SubtitleSlot.Sc, Candidate("C:\\bad\0invalid.ass"))],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DraftCodes.InvalidPath, result.ErrorCode);
        Assert.NotNull(result.Detail);
    }

    private static DraftCandidate Candidate(string path, InputLocationKind kind = InputLocationKind.LocalFile) =>
        new(new InputItem(path, kind));
}
