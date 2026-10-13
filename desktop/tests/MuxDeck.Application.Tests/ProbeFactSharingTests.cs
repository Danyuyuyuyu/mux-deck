using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class ProbeFactSharingTests
{
    [Fact]
    public void Probe_contract_and_qc_share_domain_track_values_and_collection_equality()
    {
        MediaProbeResult Create() => new("input",
            [new(new(2), TrackKind.Video, "video", "AVC", null, new("eng"), null,
                "", new(false, false), null, null, null, false, false)], [], null);
        var first = Create();
        var second = Create();
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var qc = new MediaQcFacts(first.Tracks, first.DurationNanoseconds);
        Assert.Equal(first.Tracks, qc.Tracks);
        Assert.Equal(typeof(MediaQcFacts).Assembly, typeof(MediaTrackInfo).Assembly);
        Assert.Null(OutputQcRules.CheckTracks(qc, new(second.Tracks, null), [], [], []).Error);
    }
}
