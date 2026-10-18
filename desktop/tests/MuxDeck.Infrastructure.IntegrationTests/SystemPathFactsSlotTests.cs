using MuxDeck.Application.Drafts;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

// 4b-2 固定槽位接纳事实：旧 VM 先裁剪完整路径，再判断存在性、扩展名与语法。
public sealed class SystemPathFactsSlotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4b2-slot-" + Guid.NewGuid().ToString("N"));

    public SystemPathFactsSlotTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void File_trailing_separator_is_trimmed_before_existence_and_extension_checks()
    {
        var file = Write("plain.ass");
        foreach (var separator in new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
        {
            var facts = SystemPathFacts.Instance.CollectForFixedSlot(new InputItem(file + separator, InputLocationKind.LocalFile));
            Assert.Equal(Path.TrimEndingDirectorySeparator(file), facts.FullPath);
            Assert.True(facts.FileExists);
            Assert.Equal(".ass", facts.Extension);
            Assert.Null(facts.FailureDetail);
        }
    }

    [Fact]
    public void Directory_trailing_separator_is_trimmed_before_existence_check()
    {
        var directory = Path.Combine(root, "输出 目录");
        Directory.CreateDirectory(directory);

        var facts = SystemPathFacts.Instance.CollectForFixedSlot(
            new InputItem(directory + Path.DirectorySeparatorChar, InputLocationKind.LocalDirectory));

        Assert.Equal(directory, facts.FullPath);
        Assert.True(facts.DirectoryExists);
        Assert.Null(facts.FailureDetail);
    }

    [Fact]
    public async Task Committing_the_same_directory_with_a_trailing_separator_does_not_bump_revision()
    {
        var directory = Path.Combine(root, "输出");
        Directory.CreateDirectory(directory);
        var draft = new MuxDraft(SystemPathFacts.Instance);
        draft.SetOutputDirectory(directory);
        var revision = draft.DraftRevision;

        var prepared = draft.Prepare(DraftOperation.OutputDirectory,
            [new(new InputItem(directory + Path.DirectorySeparatorChar, InputLocationKind.LocalDirectory))], []);
        Assert.True(prepared.Succeeded);
        Assert.True(draft.Commit(prepared.Plan!).Succeeded);

        Assert.Equal(revision, draft.DraftRevision);
        Assert.Equal(directory, draft.OutputDirectory);
    }

    [Fact]
    public void Committing_the_same_directory_with_a_trailing_current_path_does_not_bump_revision()
    {
        var directory = Path.Combine(root, "当前输出");
        Directory.CreateDirectory(directory);
        var draft = new MuxDraft(SystemPathFacts.Instance);
        draft.SetOutputDirectory(directory + Path.DirectorySeparatorChar);
        var revision = draft.DraftRevision;

        var prepared = draft.Prepare(DraftOperation.OutputDirectory,
            [new(new InputItem(directory, InputLocationKind.LocalDirectory))], []);
        Assert.True(prepared.Succeeded);
        Assert.True(draft.Commit(prepared.Plan!).Succeeded);

        Assert.Equal(revision, draft.DraftRevision);
    }

    [Fact]
    public void Dot_segments_and_forward_slashes_resolve_like_the_legacy_path_normalization()
    {
        var file = Write("a.ass");
        var mixed = root.Replace('\\', '/') + "/sub/../a.ass";

        var facts = SystemPathFacts.Instance.CollectForFixedSlot(new InputItem(mixed, InputLocationKind.LocalFile));

        Assert.Equal(Path.TrimEndingDirectorySeparator(file), facts.FullPath);
        Assert.True(facts.FileExists);
        Assert.Equal(".ass", facts.Extension);
        Assert.Null(facts.FailureDetail);
    }

    [Fact]
    public void Drive_root_is_not_trimmed()
    {
        var facts = SystemPathFacts.Instance.CollectForFixedSlot(new InputItem(@"C:\", InputLocationKind.LocalDirectory));

        Assert.Equal(@"C:\", facts.FullPath);
        Assert.True(facts.DirectoryExists);
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    public void Trailing_dot_or_space_matches_the_legacy_candidate_validation(string suffix)
    {
        var file = Write("trail.ass");
        var input = file + suffix;
        var item = new InputItem(input, InputLocationKind.LocalFile);
        var facts = SystemPathFacts.Instance.CollectForFixedSlot(item);
        var legacy = LegacyFacts(item);

        Assert.Equal(legacy.FullPath, facts.FullPath);
        Assert.Equal(legacy.Extension, facts.Extension);
        Assert.Equal(legacy.FileExists, facts.FileExists);
        Assert.Equal(legacy.ContainsInvalidPathChars, facts.ContainsInvalidPathChars);

        var draft = new MuxDraft(SystemPathFacts.Instance);
        var result = draft.Prepare(DraftOperation.Subtitles, [], [new(SubtitleSlot.Sc, new DraftCandidate(item))]);
        Assert.Equal(LegacyCode(item), result.ErrorCode);
    }

    [Fact]
    public void Legacy_import_collection_still_keeps_the_trailing_separator_semantics()
    {
        var file = Write("plain.ass");
        var item = new InputItem(file + Path.DirectorySeparatorChar, InputLocationKind.LocalFile);

        var legacy = SystemPathFacts.Instance.Collect(item);
        var fixedSlot = SystemPathFacts.Instance.CollectForFixedSlot(item);
        var reference = LegacyImportFacts(item);

        Assert.Equal(reference.FullPath, legacy.FullPath);
        Assert.Equal(reference.Extension, legacy.Extension);
        Assert.Equal(reference.FileExists, legacy.FileExists);
        Assert.Equal(reference.DirectoryExists, legacy.DirectoryExists);
        Assert.Equal(reference.ContainsInvalidPathChars, legacy.ContainsInvalidPathChars);
        // 固定槽位裁剪末尾分隔符是旧 ValidateCandidate 的语义，导入事实保持不变。
        Assert.Equal(Path.TrimEndingDirectorySeparator(file), fixedSlot.FullPath);
    }

    private string Write(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "");
        return path;
    }

    private static InputPathFacts LegacyImportFacts(InputItem item)
    {
        static string Normalize(string path, ref string? detail)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException
                or PathTooLongException or System.Security.SecurityException)
            { detail ??= exception.Message; return path; }
        }
        string? detail = null;
        var directoryInput = item.Kind == InputLocationKind.LocalDirectory
            ? item.Path : Path.GetDirectoryName(item.Path) ?? "";
        var directory = Normalize(directoryInput, ref detail);
        var normalized = Normalize(item.Path, ref detail);
        return new(item.Path, Path.IsPathFullyQualified(item.Path), normalized, Path.GetExtension(item.Path),
            directory, item.Kind, File.Exists(item.Path), Directory.Exists(item.Path),
            normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, detail);
    }

    private static InputPathFacts LegacyFacts(InputItem item)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path));
            return new(item.Path, Path.IsPathFullyQualified(item.Path), normalized, Path.GetExtension(normalized),
                Path.GetDirectoryName(normalized) ?? "", item.Kind, File.Exists(normalized), Directory.Exists(normalized),
                normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException
            or UnauthorizedAccessException)
        {
            return new(item.Path, Path.IsPathFullyQualified(item.Path), item.Path, "", "", item.Kind, false, false, false,
                exception.Message);
        }
    }

    private static string? LegacyCode(InputItem item)
    {
        var facts = LegacyFacts(item);
        if (!InputRules.AcceptsFixedSlotPath(facts.Path, facts.IsFullyQualified)) return DraftCodes.InvalidPath;
        if (!string.IsNullOrWhiteSpace(facts.FailureDetail)) return DraftCodes.InvalidPath;
        if (!InputRules.AcceptsNormalizedPathSyntax(facts.FullPath, facts.ContainsInvalidPathChars)) return DraftCodes.InvalidPath;
        if (!facts.FileExists) return DraftCodes.MissingPath;
        if (!InputRules.AcceptsFixedSlotExtension(facts.Extension, isVideo: false)) return DraftCodes.WrongType;
        return null;
    }
}
