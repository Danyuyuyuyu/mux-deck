using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    // 检查 restore 的实际依赖图，避免只检查 csproj 而漏掉共享 props 注入的引用。
    [Theory]
    [InlineData("src/MuxDeck.Domain", new string[0])]
    [InlineData("src/MuxDeck.Application", new[] { "MuxDeck.Domain" })]
    [InlineData("src/MuxDeck.Infrastructure", new[] { "MuxDeck.Application", "MuxDeck.Domain" })]
    [InlineData("src/MuxDeck.Core", new[] { "MuxDeck.Application", "MuxDeck.Domain", "MuxDeck.Infrastructure" })]
    [InlineData("src/MuxDeck.Desktop", new[] { "MuxDeck.Application", "MuxDeck.Core" })]
    [InlineData("tests/MuxDeck.Core.Tests", new[] { "MuxDeck.Core" })]
    [InlineData("tests/MuxDeck.Domain.Tests", new[] { "MuxDeck.Domain" })]
    [InlineData("tests/MuxDeck.Application.Tests", new[] { "MuxDeck.Application" })]
    [InlineData("tests/MuxDeck.Architecture.Tests", new[] { "MuxDeck.Application", "MuxDeck.Core", "MuxDeck.Desktop", "MuxDeck.Domain", "MuxDeck.Infrastructure" })]
    [InlineData("tests/MuxDeck.Infrastructure.IntegrationTests", new[] { "MuxDeck.Infrastructure" })]
    [InlineData("tests/MuxDeck.Smoke", new[] { "MuxDeck.Desktop" })]
    public void Projects_reference_only_the_layers_they_verify_or_use(string project, string[] expected)
    {
        using var assets = ArchitectureScope.ReadAssets(project);
        foreach (var framework in assets.RootElement.GetProperty("project").GetProperty("restore")
                     .GetProperty("frameworks").EnumerateObject())
        {
            var actual = framework.Value.GetProperty("projectReferences").EnumerateObject()
                .Select(reference => Path.GetRelativePath(
                    Path.Combine(ArchitectureScope.Root, "src"),
                    reference.Value.GetProperty("projectPath").GetString()!).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal).ToArray();
            var expectedPaths = expected.Select(name => $"{name}/{name}.csproj")
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedPaths, actual);
        }
    }

    [Fact]
    public void Domain_has_no_third_party_packages_or_extra_frameworks()
    {
        using var assets = ArchitectureScope.ReadAssets("src/MuxDeck.Domain");
        Assert.DoesNotContain(assets.RootElement.GetProperty("libraries").EnumerateObject(),
            library => library.Value.GetProperty("type").GetString() == "package");
        foreach (var framework in assets.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("dependencies", out var dependencies))
                Assert.Empty(dependencies.EnumerateObject());
            Assert.Equal(new[] { "Microsoft.NETCore.App" },
                framework.Value.GetProperty("frameworkReferences").EnumerateObject().Select(reference => reference.Name));
        }
    }

    [Theory]
    [InlineData("src/MuxDeck.Domain")]
    [InlineData("src/MuxDeck.Application")]
    public void Inner_layers_have_no_assembly_file_references(string project)
    {
        var name = Path.GetFileName(project);
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(ArchitectureScope.Root, project, name + ".csproj"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "Reference");
    }
}

public sealed class NamespaceDependencyTests
{
    [Fact]
    public void Domain_assembly_depends_only_on_system_and_domain()
    {
        ArchitectureScope.AssertSuccess(Types.InAssembly(Assembly.Load("MuxDeck.Domain"))
            .Should().OnlyHaveDependenciesOn("System", "MuxDeck.Domain").GetResult());
    }

    [Fact]
    public void Application_assembly_depends_only_on_system_domain_and_its_own_types()
    {
        ArchitectureScope.AssertSuccess(Types.InAssembly(Assembly.Load("MuxDeck.Application"))
            .Should().OnlyHaveDependenciesOn("System", "MuxDeck.Domain", "MuxDeck.Contracts", "MuxDeck.Application")
            .GetResult());
    }

    [Fact]
    public void Every_external_port_has_an_infrastructure_implementation()
    {
        var ports = new[]
        {
            typeof(MuxDeck.Application.Ports.IMediaProber), typeof(MuxDeck.Application.Ports.IFontSubsetter),
            typeof(MuxDeck.Application.Ports.IMuxer), typeof(MuxDeck.Application.Ports.IMediaExtractor),
            typeof(MuxDeck.Application.Ports.IFontInspector), typeof(MuxDeck.Application.Ports.IArtifactPublisher),
            typeof(MuxDeck.Application.Ports.ITaskWorkspaceFactory), typeof(MuxDeck.Application.Ports.IToolCatalog),
            typeof(MuxDeck.Application.Ports.IClock), typeof(MuxDeck.Application.Ports.ITaskWorkspace)
        };
        var implementations = Assembly.Load("MuxDeck.Infrastructure").GetTypes();
        foreach (var port in ports)
            Assert.Contains(implementations, type => type.IsClass && !type.IsAbstract && port.IsAssignableFrom(type));
    }

    [Fact]
    public void Infrastructure_assembly_does_not_depend_on_desktop_or_legacy_core()
    {
        ArchitectureScope.AssertSuccess(Types.InAssembly(Assembly.Load("MuxDeck.Infrastructure"))
            .ShouldNot().HaveDependencyOnAny("MuxDeck.Desktop", "MuxDeck.Core", "Avalonia").GetResult());
    }

    // Core 中先改命名空间再搬迁的类型也必须接受同样的依赖检查。
    [Fact]
    public void Domain_namespace_depends_only_on_system_and_domain()
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace("MuxDeck.Domain").Should()
            .OnlyHaveDependenciesOn("System", "MuxDeck.Domain").GetResult());
    }

    [Fact]
    public void Application_namespace_depends_only_on_system_domain_and_its_own_types()
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace("MuxDeck.Application").Should()
            .OnlyHaveDependenciesOn("System", "MuxDeck.Domain", "MuxDeck.Contracts", "MuxDeck.Application")
            .GetResult());
    }

    [Fact]
    public void Infrastructure_namespace_does_not_depend_on_desktop_or_legacy_core()
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace("MuxDeck.Infrastructure").ShouldNot()
            .HaveDependencyOnAny("MuxDeck.Desktop", "MuxDeck.Core", "Avalonia").GetResult());
    }

    [Fact]
    public void Contracts_depend_only_on_system_domain_and_other_contracts()
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace("MuxDeck.Contracts").Should()
            .OnlyHaveDependenciesOn("System", "MuxDeck.Domain", "MuxDeck.Contracts").GetResult());
    }

    [Theory]
    [InlineData("MuxDeck.Domain", new[] { "System.IO", "System.Diagnostics.Process", "System.Net", "Avalonia", "System.Console" })]
    [InlineData("MuxDeck.Application", new[] { "Avalonia", "MuxDeck.Infrastructure", "MuxDeck.Desktop", "MuxDeck.Core", "System.Diagnostics.Process", "System.Net" })]
    [InlineData("MuxDeck.Contracts", new[] { "System.Diagnostics.Process", "System.Net" })]
    public void Inner_namespaces_do_not_use_side_effects_or_outer_layers(string layer, string[] forbidden)
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace(layer).ShouldNot().HaveDependencyOnAny(forbidden).GetResult());
    }

    [Fact]
    public void Domain_does_not_read_current_time()
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace("MuxDeck.Domain").Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
    }

    [Theory]
    [InlineData("MuxDeck.Desktop.ViewModels")]
    [InlineData("MuxDeck.Desktop.Views")]
    public void Views_and_view_models_do_not_use_infrastructure(string layer)
    {
        var types = ArchitectureScope.ProductionTypes().That().ResideInNamespace(layer);
        Assert.NotEmpty(types.GetTypes());
        ArchitectureScope.AssertSuccess(types.ShouldNot().HaveDependencyOn("MuxDeck.Infrastructure").GetResult());
    }

    [Theory]
    [InlineData("MuxDeck.Desktop.ViewModels")]
    [InlineData("MuxDeck.Desktop.Views")]
    public void Views_and_view_models_use_application_only_through_contracts(string layer)
    {
        // 用例接口归 MuxDeck.Contracts；Application 的端口接口与内部实现均不向视图开放。
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace(layer).ShouldNot().HaveDependencyOn("MuxDeck.Application").GetResult());
    }


    [Theory]
    [InlineData("MuxDeck.Application")]
    [InlineData("MuxDeck.Contracts")]
    public void Application_and_contracts_do_not_read_time_or_generate_ids(string layer)
    {
        ArchitectureScope.AssertSuccess(ArchitectureScope.ProductionTypes().That()
            .ResideInNamespace(layer).Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
    }

    [Fact]
    public void Only_program_and_composition_can_use_infrastructure()
    {
        var rule = new CompositionEntryRule();
        var result = Types.InAssembly(Assembly.Load("MuxDeck.Desktop")).That()
            .HaveDependencyOn("MuxDeck.Infrastructure").Should().MeetCustomRule(rule).GetResult();
        ArchitectureScope.AssertSuccess(result, rule.FailureReason);
    }
}

internal static class ArchitectureScope
{
    private static readonly string[] ProductionNames =
        ["MuxDeck.Domain", "MuxDeck.Application", "MuxDeck.Infrastructure", "MuxDeck.Core", "MuxDeck.Desktop"];

    internal static string Root { get; } = FindRoot();

    internal static Types ProductionTypes() => Types.InAssemblies(ProductionNames.Select(Assembly.Load));

    internal static JsonDocument ReadAssets(string project) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, project, "obj", "project.assets.json")));

    internal static bool InNamespace(string? actual, string expected) =>
        actual == expected || actual?.StartsWith(expected + ".", StringComparison.Ordinal) == true;

    internal static void AssertSuccess(NetArchTest.Rules.TestResult result, string? detail = null) =>
        Assert.True(result.IsSuccessful, "依赖规则违反：" +
            string.Join(", ", result.FailingTypeNames ?? []) + " " + detail);

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "MuxDeck.sln")))
                return folder.FullName;
        throw new DirectoryNotFoundException("找不到 desktop/MuxDeck.sln；架构测试需要在仓库 checkout 内运行。");
    }
}
