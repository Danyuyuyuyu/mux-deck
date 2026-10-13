using MuxDeck.Domain;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class DomainRuleBoundaryTests
{
    // 逐个确认迁入的真实规则存在，防止空命名空间导致副作用检查虚假通过。
    [Theory]
    [InlineData(typeof(TrackPolicy))]
    [InlineData(typeof(LanguageRules))]
    [InlineData(typeof(OutputNaming))]
    public void Extracted_rules_use_only_supplied_facts_and_no_side_effect_apis(Type ruleType)
    {
        Assert.Equal("MuxDeck.Domain", ruleType.Namespace);
        Assert.Equal("MuxDeck.Domain", ruleType.Assembly.GetName().Name);
        var types = Types.InAssembly(ruleType.Assembly).That().HaveName(ruleType.Name);
        Assert.Single(types.GetTypes());
        ArchitectureScope.AssertSuccess(types.ShouldNot().HaveDependencyOnAny(
            "System.IO", "System.Diagnostics.Process", "System.Net", "System.Console",
            "System.Environment", "Avalonia").GetResult());
        ArchitectureScope.AssertSuccess(types.Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
    }
}
