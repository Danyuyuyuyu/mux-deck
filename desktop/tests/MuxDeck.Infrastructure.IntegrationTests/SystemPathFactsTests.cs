using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class SystemPathFactsTests
{
    private const string InvalidPath = "C:\\bad\0invalid.mkv";

    [Fact]
    public void Invalid_path_keeps_the_legacy_facts_and_only_adds_failure_detail()
    {
        var item = new InputItem(InvalidPath, InputLocationKind.LocalFile);
        var actual = Assert.Single(SystemPathFacts.Instance.Collect([item]));
        var legacy = LegacyCollect(item);

        Assert.Equal(legacy.Path, actual.Path);
        Assert.Equal(legacy.IsFullyQualified, actual.IsFullyQualified);
        Assert.Equal(legacy.FullPath, actual.FullPath);
        Assert.Equal(legacy.Extension, actual.Extension);
        Assert.Equal(legacy.Directory, actual.Directory);
        Assert.Equal(legacy.Kind, actual.Kind);
        Assert.Equal(legacy.FileExists, actual.FileExists);
        Assert.Equal(legacy.DirectoryExists, actual.DirectoryExists);
        Assert.Equal(legacy.ContainsInvalidPathChars, actual.ContainsInvalidPathChars);
        Assert.NotNull(actual.FailureDetail);
    }

    [Fact]
    public async Task Invalid_path_keeps_the_legacy_prepare_import_error()
    {
        var item = new InputItem(InvalidPath, InputLocationKind.LocalFile);
        var actualFacts = Assert.Single(SystemPathFacts.Instance.Collect([item]));
        var legacyFacts = LegacyCollect(item);
        var request = new ImportRequest(Guid.NewGuid(), 1,
            new EquatableArray<InputItem>([item]), null);
        var tools = new ToolConfiguration("tools", "logs", "cache");

        var actual = await Create(actualFacts).PrepareAsync(request, tools, TestContext.Current.CancellationToken);
        var legacy = await Create(legacyFacts).PrepareAsync(request, tools, TestContext.Current.CancellationToken);

        Assert.Equal(legacy.Failure!.Code, actual.Failure!.Code);
        Assert.Equal(legacy.Failure.Detail, actual.Failure.Detail);
    }

    private static PrepareImportUseCase Create(InputPathFacts facts) =>
        new(new NoProbe(), new NoReader(), new StaticFacts(facts));

    private static InputPathFacts LegacyCollect(InputItem item)
    {
        var directory = item.Kind == InputLocationKind.LocalDirectory
            ? LegacyNormalize(item.Path) : LegacyNormalize(Path.GetDirectoryName(item.Path) ?? "");
        var normalized = LegacyNormalize(item.Path);
        return new(item.Path, Path.IsPathFullyQualified(item.Path), normalized, Path.GetExtension(item.Path),
            directory, item.Kind, File.Exists(item.Path), Directory.Exists(item.Path),
            normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, null);
    }

    private static string LegacyNormalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return path; }
        catch (NotSupportedException) { return path; }
        catch (PathTooLongException) { return path; }
        catch (IOException) { return path; }
        catch (System.Security.SecurityException) { return path; }
    }

    private sealed class StaticFacts(InputPathFacts fact) : IPathFacts
    {
        public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EquatableArray<InputPathFacts>([fact]));
    }

    private sealed class NoProbe : IProbeMedia
    {
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("路径事实失败测试不应调用媒体探测。");
    }

    private sealed class NoReader : IMuxInputReader
    {
        public Task<MuxVideoFacts> ReadVideoAsync(string path, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<MuxFontDirectoryFacts> ReadFontDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<MuxOutputFacts> ReadOutputAsync(OutputSpec output, string videoPath, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<MuxTrackFileFacts> ReadTrackAsync(ExternalTrackSpec track, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<bool> DestinationExistsAsync(string path, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
