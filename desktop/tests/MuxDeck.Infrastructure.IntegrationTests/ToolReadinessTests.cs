using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure.Muxing;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class ToolReadinessTests
{
    [Theory]
    [InlineData(ToolCapability.FontSubsetBackend, ErrorCodes.FontBackendNotFound)]
    [InlineData(ToolCapability.InspectFonts, ErrorCodes.FontBackendNotFound)]
    [InlineData(ToolCapability.SubsetFonts, ErrorCodes.ToolNotFound)]
    [InlineData(ToolCapability.ProbeMedia, ErrorCodes.ToolNotFound)]
    public async Task Missing_backend_has_its_own_code_and_preserves_original_port_payload(ToolCapability missing, string code)
    {
        await using var fixture = await TestWorkspace.Create();
        var original = new ToolFailureException(ErrorCodes.ToolNotFound, "原始工具缺失消息", "原始详情");
        IToolReadiness port = new ToolReadiness(catalog: new MissingCatalog(fixture.Root, missing, original));
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => port.EnsureReadyAsync(true, fixture.Context, TestContext.Current.CancellationToken));
        Assert.Equal(code, failure.Code);
        Assert.Equal(original.Message, failure.Message);
        Assert.Equal(original.Detail, failure.Detail);
        if (code == ErrorCodes.FontBackendNotFound) Assert.Same(original, failure.InnerException);
        else Assert.Same(original, failure);
    }

    [Fact]
    public async Task Srt_only_task_does_not_require_font_backend()
    {
        await using var fixture = await TestWorkspace.Create();
        IToolReadiness port = new ToolReadiness(catalog: new MissingCatalog(fixture.Root, ToolCapability.FontSubsetBackend,
            new ToolFailureException(ErrorCodes.ToolNotFound, "后端不存在")));
        await port.EnsureReadyAsync(false, fixture.Context, TestContext.Current.CancellationToken);
    }

    private sealed class MissingCatalog(string root, ToolCapability missing, ToolFailureException failure) : IToolCatalog
    {
        public Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
            ToolConfiguration tools, CancellationToken cancellationToken = default) => capabilities.Contains(missing)
            ? Task.FromException<EquatableArray<ToolDescriptor>>(failure)
            : new TestCatalog(root).ResolveAsync(capabilities, tools, cancellationToken);
    }
}
