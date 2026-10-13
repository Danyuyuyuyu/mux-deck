using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class PortFailureBoundaryTests
{
    [Fact]
    public void Failure_is_one_public_application_exception_with_readonly_payload()
    {
        var type = typeof(ToolFailureException);
        Assert.Equal(typeof(IMediaProber).Assembly, type.Assembly);
        Assert.Equal("MuxDeck.Application.Ports.Failures", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed);
        Assert.Equal(typeof(Exception), type.BaseType);
        var constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(new[] { "code", "message", "detail" }, constructor.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(typeof(string), type.GetProperty("Code")!.PropertyType);
        Assert.Equal(typeof(string), type.GetProperty("Detail")!.PropertyType);
        Assert.Null(type.GetProperty("Code")!.SetMethod);
        Assert.Null(type.GetProperty("Detail")!.SetMethod);
        Assert.DoesNotContain(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            field => !field.IsInitOnly);
    }

    [Fact]
    public void Every_port_subnamespace_keeps_data_immutable_and_has_only_the_requested_failure_class()
    {
        // 既有 Ports record 测试保持原样；这里新增覆盖 Failures 等子命名空间，异常按资源失败形状单独检查。
        var types = typeof(IMediaProber).Assembly.GetTypes().Where(type =>
            type.Namespace is not null && (type.Namespace == "MuxDeck.Application.Ports" ||
                type.Namespace.StartsWith("MuxDeck.Application.Ports.", StringComparison.Ordinal)) &&
            !type.IsDefined(typeof(CompilerGeneratedAttribute)));
        Assert.All(types, type =>
        {
            if (typeof(Exception).IsAssignableFrom(type)) Assert.Equal(typeof(ToolFailureException), type);
            else Assert.Null(ContractDataRule.Failure(type));
        });
    }

    [Fact]
    public void Work_files_are_owned_by_ports_and_final_artifacts_only_leave_the_publisher()
    {
        Assert.Equal("MuxDeck.Application.Ports", typeof(WorkFile).Namespace);
        Assert.Null(ContractDataRule.Failure(typeof(WorkFile)));
        Assert.Equal(typeof(WorkFile), typeof(FontSubsetResult).GetProperty("CorrectedSubtitle")!.PropertyType);
        Assert.Equal(typeof(EquatableArray<WorkFile>), typeof(FontSubsetResult).GetProperty("Fonts")!.PropertyType);
        Assert.Equal(typeof(EquatableArray<WorkFile>), typeof(MediaMuxRequest).GetProperty("Fonts")!.PropertyType);
        Assert.Equal(typeof(WorkFile), typeof(MediaMuxRequest).GetProperty("TemporaryOutput")!.PropertyType);
        Assert.Equal(typeof(WorkFile), typeof(MediaMuxResult).GetProperty("Artifact")!.PropertyType);
        var models = typeof(IMediaProber).Assembly.GetTypes().Where(type =>
            type.Namespace == "MuxDeck.Application.Ports" && !type.IsInterface);
        Assert.All(models.SelectMany(type => type.GetProperties()),
            property => Assert.False(ContainsFinalArtifact(property.PropertyType)));
        var methods = typeof(IMediaProber).Assembly.GetTypes().Where(type =>
            type.Namespace == "MuxDeck.Application.Ports" && type.IsInterface).SelectMany(type => type.GetMethods());
        Assert.All(methods, method =>
        {
            Assert.All(method.GetParameters(), parameter => Assert.False(ContainsFinalArtifact(parameter.ParameterType)));
            if (ContainsFinalArtifact(method.ReturnType))
            {
                Assert.Equal(typeof(IArtifactPublisher), method.DeclaringType);
                Assert.Equal(nameof(IArtifactPublisher.PublishNoOverwriteAsync), method.Name);
            }
        });
        Assert.Equal(typeof(Task<OutputArtifact>),
            typeof(IArtifactPublisher).GetMethod(nameof(IArtifactPublisher.PublishNoOverwriteAsync))!.ReturnType);
    }

    [Theory]
    [InlineData("IMediaProber")]
    [InlineData("IFontSubsetter")]
    [InlineData("IMuxer")]
    [InlineData("IMediaExtractor")]
    [InlineData("IFontInspector")]
    [InlineData("IArtifactPublisher")]
    [InlineData("ITaskWorkspaceFactory")]
    [InlineData("ITaskWorkspace")]
    [InlineData("IToolCatalog")]
    [InlineData("IClock")]
    public void Each_port_and_declared_member_documents_failure_and_cancellation(string name)
    {
        var type = typeof(IMediaProber).Assembly.GetType("MuxDeck.Application.Ports." + name)!;
        var documents = ReadPortDocuments();
        AssertFailureDocumentation(Assert.Single(documents, item =>
            item.Declaration.Contains("public interface " + name, StringComparison.Ordinal) &&
            Regex.IsMatch(item.Declaration, @"\b" + name + @"\b")).Document);
        foreach (var method in type.GetMethods().Where(method => !method.IsSpecialName))
            AssertFailureDocumentation(Assert.Single(documents, item =>
                Regex.IsMatch(item.Declaration, @"\b" + method.Name + @"\s*\(")).Document);
        foreach (var property in type.GetProperties())
            AssertFailureDocumentation(Assert.Single(documents, item =>
                Regex.IsMatch(item.Declaration, @"\b" + property.Name + @"\s*\{")).Document);
    }

    [Theory]
    [InlineData(typeof(List<WorkFile>), false)]
    [InlineData(typeof(EquatableArray<OutputArtifact>), true)]
    [InlineData(typeof(Task<OutputArtifact>), true)]
    [InlineData(typeof(OutputArtifact), true)]
    public void Final_artifact_guard_checks_nested_generic_signatures(Type type, bool expected)
    {
        Assert.Equal(expected, ContainsFinalArtifact(type));
    }

    private static bool ContainsFinalArtifact(Type type) => type == typeof(OutputArtifact) ||
        type.HasElementType && ContainsFinalArtifact(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericArguments().Any(ContainsFinalArtifact);

    private static void AssertFailureDocumentation(XElement document)
    {
        var failure = Assert.Single(document.Elements("exception"),
            item => item.Attribute("cref")?.Value == nameof(ToolFailureException));
        Assert.Contains("工具退出码异常", failure.Value);
        Assert.Contains("输出解析失败", failure.Value);
        Assert.Contains("Fatal", failure.Value);
        var cancelled = Assert.Single(document.Elements("exception"),
            item => item.Attribute("cref")?.Value == nameof(OperationCanceledException));
        Assert.Contains("取消", cancelled.Value);
    }

    private static (string Declaration, XElement Document)[] ReadPortDocuments()
    {
        var root = Path.Combine(ArchitectureScope.Root, "src", "MuxDeck.Application", "Ports");
        var pattern = @"(?m)(?<doc>(?:^[ \t]*///[^\r\n]*\r?\n)+)[ \t]*(?<declaration>[^\r\n]+)";
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).SelectMany(path =>
            Regex.Matches(File.ReadAllText(path), pattern).Cast<Match>().Select(match =>
            {
                var xml = Regex.Replace(match.Groups["doc"].Value, @"(?m)^[ \t]*/// ?", "");
                return (match.Groups["declaration"].Value, XElement.Parse("<member>" + xml + "</member>"));
            })).ToArray();
    }
}
