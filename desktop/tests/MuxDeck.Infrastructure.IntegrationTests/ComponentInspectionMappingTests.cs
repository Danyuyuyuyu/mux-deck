using MuxDeck.Application;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Components;
using Xunit;
using C = MuxDeck.Contracts;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class ComponentInspectionMappingTests
{
    // 契约枚举替换了旧的显示文字；逐值确认字符串仍映射到原枚举，未知值落到原来的默认项。
    [Fact]
    public async Task Legacy_purpose_and_source_strings_map_to_every_contract_enum()
    {
        var configuration = new C.ComponentConfiguration("root", Path.Combine(Path.GetTempPath(), "MuxDeck-tools"), "logs", "cache");
        C.IManageComponents manager = new ManageComponentsUseCase(
            new InfrastructureComponentManager(new MappingVerifier(), new ComponentInstaller(), new ComponentUninstaller()),
            new OperationCoordinator());
        var result = await manager.InspectAsync("assfonts", configuration, TestContext.Current.CancellationToken);
        Assert.Equal(new[]
        {
            C.ComponentPartPurpose.PackagingAndProbe, C.ComponentPartPurpose.SubtitleFontChapterExtraction,
            C.ComponentPartPurpose.TrackMetadataRepair, C.ComponentPartPurpose.SubtitleRenderPreview,
            C.ComponentPartPurpose.PrimaryFontSubsetting, C.ComponentPartPurpose.FallbackFontSubsetting
        }, result.Parts.Take(6).Select(part => part.Purpose));
        Assert.Equal(new[] { C.ComponentPartSource.MuxDeck, C.ComponentPartSource.Path, C.ComponentPartSource.System },
            result.Parts.Skip(6).Take(3).Select(part => part.Source));
        var unknown = result.Parts[9];
        Assert.Equal(C.ComponentPartPurpose.Unknown, unknown.Purpose);
        Assert.Equal(C.ComponentPartSource.Missing, unknown.Source);
    }

    private sealed class MappingVerifier : IComponentVerifier
    {
        private static readonly string[] Purposes =
            ["封装与轨道探测", "字幕、字体与章节提取", "轨道与容器元数据修补", "字幕渲染与预览", "字体子集化主工具", "字体子集化备用工具"];
        private static readonly string[] Sources = ["Mux Deck", "PATH", "系统"];

        public Task<ComponentInspection> VerifyAsync(ComponentDefinition component, C.ComponentConfiguration configuration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = Purposes.Select(purpose => new ToolStatus(purpose, purpose, configuration.ToolsDirectory) { Source = "Mux Deck" })
                .Concat(Sources.Select(source => new ToolStatus(source, "封装与轨道探测", configuration.ToolsDirectory) { Source = source }))
                .Append(new ToolStatus("未知", "未知用途", configuration.ToolsDirectory) { Source = "未知来源" })
                .ToArray();
            return Task.FromResult(new ComponentInspection(ComponentState.Available, parts, "1.0"));
        }
    }
}
