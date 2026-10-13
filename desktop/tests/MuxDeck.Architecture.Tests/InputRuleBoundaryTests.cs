using MuxDeck.Domain;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class InputRuleBoundaryTests
{
    [Fact]
    public void Input_rules_use_facts_without_files_directories_processes_or_current_time()
    {
        Assert.Equal("MuxDeck.Domain", typeof(InputRules).Assembly.GetName().Name);
        var rules = Types.InAssembly(typeof(InputRules).Assembly).That().HaveName(nameof(InputRules));
        Assert.Single(rules.GetTypes());
        ArchitectureScope.AssertSuccess(rules.ShouldNot().HaveDependencyOnAny(
            "System.IO", "System.Diagnostics.Process", "System.Net", "System.Console",
            "System.Environment", "Avalonia").GetResult());
        ArchitectureScope.AssertSuccess(rules.Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
    }
}
