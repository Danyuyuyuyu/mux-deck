using MuxDeck.Domain;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class DiagnosticAndQcBoundaryTests
{
    [Theory]
    [InlineData(typeof(DiagnosticPolicy))]
    [InlineData(typeof(OutputQcRules))]
    [InlineData(typeof(AssDocument))]
    public void Pure_diagnostic_qc_and_ass_rules_have_no_side_effect_dependencies(Type type)
    {
        Assert.Equal("MuxDeck.Domain", type.Assembly.GetName().Name);
        var rules = Types.InAssembly(type.Assembly).That().HaveName(type.Name);
        Assert.Single(rules.GetTypes());
        ArchitectureScope.AssertSuccess(rules.ShouldNot().HaveDependencyOnAny(
            "System.IO", "System.Diagnostics.Process", "System.Net", "System.Console",
            "Avalonia", "MuxDeck.Core", "MuxDeck.Contracts").GetResult());
        ArchitectureScope.AssertSuccess(rules.Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
        ArchitectureScope.AssertSuccess(rules.Should().MeetCustomRule(new NoEnvironmentSideEffectsRule()).GetResult());
    }
}
