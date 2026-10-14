using MuxDeck.Core;
using Xunit;

namespace MuxDeck.Core.Tests;

// PATH 是进程级状态；该集合不与本测试进程的其他集合并行。
[CollectionDefinition("Tool locator environment", DisableParallelization = true)]
public sealed class ToolLocatorEnvironmentCollection { }

[Collection("Tool locator environment")]
public sealed class ToolLocatorSourceTests
{
    [Theory]
    [InlineData("bundle", "Mux Deck")]
    [InlineData("path", "PATH")]
    [InlineData("missing", "未找到")]
    public void Public_inspection_preserves_location_priority_and_source_display(string location, string expectedSource)
    {
        var root = Path.Combine(Path.GetTempPath(), "MuxDeck-Locator-" + Guid.NewGuid().ToString("N"));
        var tools = Path.Combine(root, "tools");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(tools); Directory.CreateDirectory(external);
        var executable = "AssFontSubset.Console" + (OperatingSystem.IsWindows() ? ".exe" : "");
        var bundled = Path.Combine(tools, "assfontsubset", "AssFontSubset.Console.exe");
        var onPath = Path.Combine(external, executable);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", external);
            if (location != "missing") File.WriteAllText(onPath, "external");
            if (location == "bundle")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
                File.WriteAllText(bundled, "bundled");
            }
            var status = new ToolLocator(new(root) { ToolsDirectoryOverride = tools }).Inspect()
                .Single(tool => tool.Name == "AssFontSubset");
            Assert.Equal(expectedSource, status.Source);
            Assert.Equal(location == "bundle" ? bundled : location == "path" ? onPath : null, status.Path);
            Assert.Equal(location != "missing", status.IsAvailable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(root, recursive: true);
        }
    }
}
