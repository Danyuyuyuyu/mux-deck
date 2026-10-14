using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Components;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class ComponentDirectoryTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "MuxDeck", "inside");

    [Fact]
    public void IsInside_rejects_prefix_siblings_that_only_share_the_root_text()
    {
        var root = Path.Combine(Root, "afs");
        Assert.True(ComponentDirectories.IsInside(root, Path.Combine(root, "assfonts.exe")));
        // 仅判断严格子路径，根本身不算“在内部”。
        Assert.False(ComponentDirectories.IsInside(root, root));
        Assert.False(ComponentDirectories.IsInside(root, Path.Combine(Root, "afs-old", "assfonts.exe")));
    }

    [Fact]
    public void IsInside_tolerates_a_trailing_separator_on_the_root()
    {
        var root = Path.Combine(Root, "afs");
        Assert.True(ComponentDirectories.IsInside(root + Path.DirectorySeparatorChar, Path.Combine(root, "sub", "x.exe")));
        Assert.False(ComponentDirectories.IsInside(root + Path.DirectorySeparatorChar, Path.Combine(Root, "afs-old", "x.exe")));
    }

    [Fact]
    public void IsInside_normalizes_relative_segments_before_comparing()
    {
        var root = Path.Combine(Root, "afs");
        Assert.True(ComponentDirectories.IsInside(root, Path.Combine(root, "sub", "..", "x.exe")));
        Assert.False(ComponentDirectories.IsInside(root, Path.Combine(root, "..", "other", "x.exe")));
    }

    [Fact]
    public void IsInside_follows_the_platform_case_rule()
    {
        var root = Path.Combine(Root, "Afs");
        var differentCase = Path.Combine(Root, "aFS", "x.exe");
        Assert.Equal(OperatingSystem.IsWindows(), ComponentDirectories.IsInside(root, differentCase));
    }

    [Fact]
    public void ForRoot_normalizes_dot_segments_and_keeps_child_directories_consistent()
    {
        var work = Path.Combine(Root, "data", "..", "data");
        var tools = Path.Combine(Root, "bin");
        var paths = StoragePaths.ForRoot(work, tools);
        Assert.Equal(Path.GetFullPath(work), paths.AppDataRoot);
        Assert.Equal(Path.GetFullPath(tools), paths.ToolsDirectory);
        Assert.Equal(Path.Combine(paths.AppDataRoot, "config"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine(paths.AppDataRoot, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(paths.AppDataRoot, "cache"), paths.CacheDirectory);
    }
}
