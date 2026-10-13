using System.Reflection;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class PortBoundaryTests
{
    [Theory]
    [InlineData("IMediaProber")]
    [InlineData("IFontSubsetter")]
    [InlineData("IMuxer")]
    [InlineData("IMediaExtractor")]
    [InlineData("IFontInspector")]
    [InlineData("IArtifactPublisher")]
    [InlineData("ITaskWorkspaceFactory")]
    [InlineData("IToolCatalog")]
    [InlineData("IClock")]
    public void Required_business_ports_are_public_interfaces_owned_by_application(string name)
    {
        var assembly = Assembly.Load("MuxDeck.Application");
        var type = Assert.Single(assembly.GetTypes(), type => type.Name == name);
        Assert.Equal("MuxDeck.Application.Ports", type.Namespace);
        Assert.True(type.IsInterface && type.IsPublic);
        ArchitectureScope.AssertSuccess(Types.InAssembly(assembly).That().HaveName(name)
            .Should().OnlyHaveDependenciesOn("System", "MuxDeck.Domain", "MuxDeck.Contracts", "MuxDeck.Application")
            .GetResult());
    }

    [Fact]
    public void Workspace_exposes_readonly_paths_and_async_resource_lifetime()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ITaskWorkspace)));
        Assert.All(typeof(ITaskWorkspace).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Equal(typeof(LogRef), typeof(ITaskWorkspace).GetProperty("Log")!.PropertyType);
        Assert.Equal(typeof(Task<ITaskWorkspace>),
            typeof(ITaskWorkspaceFactory).GetMethod("CreateAsync")!.ReturnType);
    }

    [Fact]
    public void Port_data_remains_immutable_and_does_not_escape_into_public_contracts()
    {
        var assembly = Assembly.Load("MuxDeck.Application");
        var models = assembly.GetTypes().Where(type => type.Namespace == "MuxDeck.Application.Ports" && !type.IsInterface).ToArray();
        Assert.NotEmpty(models);
        Assert.All(models, type => Assert.Null(ContractDataRule.Failure(type)));
        ArchitectureScope.AssertSuccess(Types.InAssembly(assembly).That().ResideInNamespace("MuxDeck.Contracts")
            .ShouldNot().HaveDependencyOn("MuxDeck.Application.Ports").GetResult());
    }

    [Fact]
    public void Track_facts_have_one_domain_owner_and_no_duplicate_qc_type()
    {
        Assert.Equal("MuxDeck.Domain", typeof(MediaTrackInfo).Namespace);
        Assert.Equal(Assembly.Load("MuxDeck.Domain"), typeof(MediaTrackInfo).Assembly);
        Assert.Null(Assembly.Load("MuxDeck.Application").GetType("MuxDeck.Contracts.MediaTrackInfo"));
        Assert.DoesNotContain(ArchitectureScope.ProductionTypes().GetTypes(), type => type.Name == "TrackQcFacts");
        Assert.Equal(typeof(EquatableArray<MediaTrackInfo>), typeof(MediaQcFacts).GetProperty("Tracks")!.PropertyType);
        Assert.Equal(typeof(EquatableArray<MediaTrackInfo>), typeof(MediaProbeResult).GetProperty("Tracks")!.PropertyType);
    }

    [Fact]
    public void Effect_ports_accept_cancellation_and_clock_only_exposes_an_injected_value()
    {
        var ports = typeof(IMediaProber).Assembly.GetTypes().Where(type =>
            type.Namespace == "MuxDeck.Application.Ports" && type.IsInterface && type != typeof(IClock));
        // 只读路径属性没有外部操作；取消令牌检查针对调用方法，属性另由上方只读规则检查。
        Assert.All(ports.SelectMany(type => type.GetMethods()).Where(method => !method.IsSpecialName), method =>
            Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken)));
        Assert.Equal(typeof(DateTimeOffset), typeof(IClock).GetProperty("UtcNow")!.PropertyType);
        Assert.Null(typeof(IClock).GetProperty("UtcNow")!.SetMethod);
    }
}
