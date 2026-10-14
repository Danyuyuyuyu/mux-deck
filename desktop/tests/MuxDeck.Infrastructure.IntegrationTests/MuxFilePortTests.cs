using System.Security.Cryptography;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class MuxFilePortTests
{
    private static ToolConfiguration Tools() =>
        new("", Path.Combine(Path.GetTempPath(), "MuxDeck-3c-" + Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), "MuxDeck-3c-cache-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Input_and_evidence_ports_read_copy_and_hash_owned_business_files()
    {
        var tools = Tools();
        var workspace = await new FileTaskWorkspaceFactory().CreateAsync(tools, TestContext.Current.CancellationToken);
        var work = workspace.WorkDirectory;
        var source = Path.Combine(work, "input.srt");
        const string text = "1\r\n00:00:00,000 --> 00:00:01,000\r\nHello\r\n";
        await File.WriteAllTextAsync(source, text, TestContext.Current.CancellationToken);
        var inputs = new MuxInputReader();
        var evidence = new MuxEvidenceReader();
        var context = new ToolCallContext(tools, workspace);
        try
        {
            Assert.True((await inputs.ReadVideoAsync(source, TestContext.Current.CancellationToken)).Exists);
            Assert.True((await inputs.ReadFontDirectoryAsync(work, TestContext.Current.CancellationToken)).Exists);
            var track = new ExternalTrackSpec(source, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false));
            Assert.Equal(text, (await inputs.ReadTrackAsync(track, TestContext.Current.CancellationToken)).SubtitleText);
            var copied = await evidence.CopySubtitleAsync(track, 0, context, TestContext.Current.CancellationToken);
            var read = await evidence.ReadSubtitleAsync(copied, TestContext.Current.CancellationToken);
            Assert.True(read.Exists);
            Assert.Equal(text, read.Text);
            var bytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
            var attachment = await evidence.ReadAttachmentAsync(copied, TestContext.Current.CancellationToken);
            Assert.Equal(bytes.Length, attachment.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), attachment.Sha256);
            Assert.Equal(attachment.Sha256, await evidence.ReadAudioPayloadAsync(copied, TestContext.Current.CancellationToken));
            Assert.True(Directory.Exists(await evidence.PrepareExtractionAsync(context, TestContext.Current.CancellationToken)));
            Assert.True(await inputs.DestinationExistsAsync(work, TestContext.Current.CancellationToken));
            Assert.False((await evidence.ReadSubtitleAsync(new(Path.Combine(work, "absent.srt")), TestContext.Current.CancellationToken)).Exists);
        }
        finally { await workspace.DisposeAsync(); }
        Assert.False(Directory.Exists(work));
        Assert.True(File.Exists(workspace.Log.Path));
    }

    [Theory]
    [InlineData("bad?.mkv", false)]
    [InlineData("folder/final.mkv", false)]
    [InlineData("final.mp4", false)]
    [InlineData("final.mkv", true)]
    public async Task Output_port_exposes_platform_name_and_source_directory_facts(string name, bool valid)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MuxDeck-3c-output"));
        var source = Path.Combine(root, "video.mkv");
        var facts = await new MuxInputReader().ReadOutputAsync(new(root, name), source, TestContext.Current.CancellationToken);
        Assert.Equal(valid, facts.NameValid);
        Assert.True(facts.SameSourceDirectory);
        Assert.False(facts.OverwritesSource);
    }

    [Fact]
    public async Task Missing_attachment_is_a_workspace_failure_with_original_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ttf");
        var error = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new MuxEvidenceReader().ReadAttachmentAsync(new(path), TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.WorkspaceIoFailed, error.Code);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task Cancelled_file_read_remains_cancellation()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MuxInputReader().ReadVideoAsync("input.mkv", token.Token));
    }
}
