using MuxDeck.Application;
using MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Desktop.Composition;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// Desktop 装配的边界行为放在这里：环境页只通过装配拿依赖，契约枚举到显示文字的映射逐值核对。
public sealed class ComponentDisplayBoundaryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4a2-" + Guid.NewGuid().ToString("N"));

    public ComponentDisplayBoundaryTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void Every_purpose_and_source_enum_renders_the_legacy_chinese_text()
    {
        var purposes = new (ComponentPartPurpose Value, string Text)[]
        {
            (ComponentPartPurpose.PackagingAndProbe, "封装与轨道探测"),
            (ComponentPartPurpose.SubtitleFontChapterExtraction, "字幕、字体与章节提取"),
            (ComponentPartPurpose.TrackMetadataRepair, "轨道与容器元数据修补"),
            (ComponentPartPurpose.SubtitleRenderPreview, "字幕渲染与预览"),
            (ComponentPartPurpose.PrimaryFontSubsetting, "字体子集化主工具"),
            (ComponentPartPurpose.FallbackFontSubsetting, "字体子集化备用工具"),
            (ComponentPartPurpose.Unknown, "")
        };
        var sources = new (ComponentPartSource Value, string Text)[]
        {
            (ComponentPartSource.MuxDeck, "Mux Deck"),
            (ComponentPartSource.Path, "PATH"),
            (ComponentPartSource.System, "系统"),
            (ComponentPartSource.Missing, "未找到")
        };
        var parts = purposes.Select(item => new ComponentPart("p", null, ComponentState.Found, null, item.Value, ComponentPartSource.MuxDeck))
            .Concat(sources.Select(item => new ComponentPart("s", null, ComponentState.Found, null, ComponentPartPurpose.PackagingAndProbe, item.Value)))
            .ToArray();
        var row = new EnvironmentComponentViewModel("sample", "sample", "核心组件", [new("tool", "", null)], (_, _) => false);
        row.ApplyInspection(new ComponentInspection("sample", ComponentState.Available, new EquatableArray<ComponentPart>(parts)));
        Assert.Equal(purposes.Select(item => item.Text), row.Parts.Take(purposes.Length).Select(value => value.Purpose));
        Assert.Equal(sources.Select(item => item.Text), row.Parts.Skip(purposes.Length).Select(value => value.Source));
    }

    [Fact]
    public void Environment_page_and_component_use_case_share_the_injected_coordinator()
    {
        var coordinator = new OperationCoordinator();
        Assert.Same(coordinator, CreateEnvironment(coordinator).Coordinator);
    }

    [Fact]
    public void Saving_paths_normalizes_dot_segments_like_storage_paths_for_root()
    {
        var environment = CreateEnvironment(new OperationCoordinator());
        var work = Path.Combine(root, "work", "..", "work");
        var tools = Path.Combine(root, "tools", "..", "tools");
        environment.WorkDirectoryInput = work;
        environment.ToolsDirectoryInput = tools;
        environment.SaveSettingsCommand.Execute(null);
        var expected = StoragePaths.ForRoot(work, tools);
        Assert.Equal(expected.AppDataRoot, environment.Storage.AppDataRoot);
        Assert.Equal(expected.ToolsDirectory, environment.Storage.ToolsDirectory);
        Assert.Equal(expected.ConfigDirectory, environment.Storage.ConfigDirectory);
        Assert.Equal(expected.LogsDirectory, environment.Storage.LogsDirectory);
        Assert.Equal(expected.CacheDirectory, environment.Storage.CacheDirectory);
        Assert.True(Directory.Exists(expected.ConfigDirectory) && Directory.Exists(expected.ToolsDirectory));
    }

    private EnvironmentViewModel CreateEnvironment(IOperationCoordinator coordinator)
    {
        var storage = StoragePaths.ForRoot(Path.Combine(root, "data"), Path.Combine(root, "tools"));
        var workspace = new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory };
        return AppComposition.CreateEnvironment(workspace, storage, coordinator, Path.Combine(root, "settings.json"));
    }
}
