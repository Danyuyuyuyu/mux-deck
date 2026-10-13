using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class ContractValueEqualityTests
{
    [Fact]
    public void Separately_created_specs_with_identical_content_are_equal()
    {
        MuxJobSpec Create() => new("video",
            [new("sub.srt", TrackKind.Subtitle, null, "", new("zh-Hans"), new(true, false))],
            [], new("fonts"), new("output", ""), new("tools", "logs", "cache"));
        var first = Create();
        var second = Create();
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { ExternalTracks = [] });
    }

    [Fact]
    public void Separately_created_results_with_identical_content_are_equal()
    {
        MuxJobResult Create() => new(MuxJobStatus.Failed, [],
            [new("check", CheckStatus.NotEvaluated, null, null, "原文")],
            [new("warning", DiagnosticSeverity.Warning, "消息", 2)],
            new("failure", JobStage.Unknown, "原因"), new("log"));
        var first = Create();
        var second = Create();
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Checks = [] });
    }

    [Fact]
    public void Default_and_explicit_empty_contract_collections_are_value_equal()
    {
        var first = new MuxJobResult(MuxJobStatus.Succeeded, default, default, default, null, null);
        var second = new MuxJobResult(MuxJobStatus.Succeeded, [], [], [], null, null);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Empty(first.Outputs);
    }

    [Fact]
    public void Probe_flag_presence_must_be_supplied_explicitly()
    {
        var constructor = Assert.Single(typeof(MediaTrackInfo).GetConstructors());
        Assert.All(constructor.GetParameters().Where(p => p.Name is "HasDefaultFlag" or "HasForcedFlag"),
            parameter => Assert.False(parameter.HasDefaultValue));
        var track = new MediaTrackInfo(new(0), TrackKind.Audio, "audio", "AAC", null, new("und"),
            null, "", new(false, false), null, 2, 48000, false, false);
        Assert.False(track.HasDefaultFlag);
        Assert.False(track.HasForcedFlag);
    }
}
