using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests
{
    public sealed class EnvironmentMemberRuleTests
    {
        [Fact]
        public void Domain_namespace_does_not_use_environment_side_effect_members()
        {
            var types = ArchitectureScope.ProductionTypes().That().ResideInNamespace("MuxDeck.Domain");
            Assert.NotEmpty(types.GetTypes());
            ArchitectureScope.AssertSuccess(types.Should().MeetCustomRule(new NoEnvironmentSideEffectsRule()).GetResult());
        }

        // 只扫描 fixture 的 IL，不执行任何环境写入、退出或 FailFast。
        [Theory]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ReadVariable), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ReadVariables), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ReadTargetVariable), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.WriteVariable), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ExpandVariables), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.MachineIdentity), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.UserIdentity), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.DomainIdentity), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ReadDirectory), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.WriteDirectory), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.Folder), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.FolderOption), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.Arguments), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.Command), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ExitProcess), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.FailProcess), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.FailWithException), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ProcessIdentity), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ProcessLocation), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.LambdaVariable), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.AsyncIdentity), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.VariableDelegate), false)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.YieldValues), true)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.ThreadIdentity), true)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.LineSeparator), true)]
        [InlineData(typeof(Fixtures.EnvironmentMembers.CpuCount), true)]
        public void Environment_rule_rejects_forbidden_members_and_allows_readonly_values(Type type, bool expected)
        {
            var types = Types.InAssembly(typeof(EnvironmentMemberRuleTests).Assembly).That().HaveName(type.Name);
            Assert.Single(types.GetTypes());
            var result = types.Should().MeetCustomRule(new NoEnvironmentSideEffectsRule()).GetResult();
            Assert.Equal(expected, result.IsSuccessful);
            if (!expected) Assert.Contains(type.FullName!, result.FailingTypeNames);
            else ArchitectureScope.AssertSuccess(types.Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult());
        }

        [Theory]
        [InlineData(typeof(Fixtures.Clock.TickCount))]
        [InlineData(typeof(Fixtures.Clock.TickCount64))]
        public void Tick_counts_still_fail_the_time_guard(Type type)
        {
            var types = Types.InAssembly(typeof(EnvironmentMemberRuleTests).Assembly).That().HaveName(type.Name);
            Assert.True(types.Should().MeetCustomRule(new NoEnvironmentSideEffectsRule()).GetResult().IsSuccessful);
            Assert.False(types.Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult().IsSuccessful);
        }
    }
}

namespace MuxDeck.Architecture.Tests.Fixtures.EnvironmentMembers
{
    internal sealed class ReadVariable { public string? Read() => Environment.GetEnvironmentVariable("MUX_TEST"); }
    internal sealed class ReadVariables { public object Read() => Environment.GetEnvironmentVariables(); }
    internal sealed class ReadTargetVariable { public string? Read() => Environment.GetEnvironmentVariable("MUX_TEST", EnvironmentVariableTarget.Process); }
    internal sealed class WriteVariable { public void Write() => Environment.SetEnvironmentVariable("MUX_TEST", "value"); }
    internal sealed class ExpandVariables { public string Read() => Environment.ExpandEnvironmentVariables("%MUX_TEST%"); }
    internal sealed class MachineIdentity { public string Read() => Environment.MachineName; }
    internal sealed class UserIdentity { public string Read() => Environment.UserName; }
    internal sealed class DomainIdentity { public string Read() => Environment.UserDomainName; }
    internal sealed class ReadDirectory { public string Read() => Environment.CurrentDirectory; }
    internal sealed class WriteDirectory { public void Write() => Environment.CurrentDirectory = "fixture"; }
    internal sealed class Folder { public string Read() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
    internal sealed class FolderOption { public string Read() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify); }
    internal sealed class Arguments { public string[] Read() => Environment.GetCommandLineArgs(); }
    internal sealed class Command { public string Read() => Environment.CommandLine; }
    internal sealed class ExitProcess { public void Exit() => Environment.Exit(1); }
    internal sealed class FailProcess { public void Fail() => Environment.FailFast("fixture"); }
    internal sealed class FailWithException { public void Fail() => Environment.FailFast("fixture", new Exception()); }
    internal sealed class ProcessIdentity { public int Read() => Environment.ProcessId; }
    internal sealed class ProcessLocation { public string? Read() => Environment.ProcessPath; }
    internal sealed class LambdaVariable { public Func<string?> Read() => () => Environment.GetEnvironmentVariable("MUX_TEST"); }
    internal sealed class AsyncIdentity { public async Task<string> Read() { await Task.Yield(); return Environment.MachineName; } }
    internal sealed class VariableDelegate { public Func<string, string?> Read() => Environment.GetEnvironmentVariable; }
    internal sealed class YieldValues { public IEnumerable<int> Read() { yield return 1; yield return 2; } }
    internal sealed class ThreadIdentity { public int Read() => Environment.CurrentManagedThreadId; }
    internal sealed class LineSeparator { public string Read() => Environment.NewLine; }
    internal sealed class CpuCount { public int Read() => Environment.ProcessorCount; }
}
