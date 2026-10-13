using System.Collections;
using System.Collections.Immutable;
using MuxDeck.Domain;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests
{
    public sealed class GuardrailCounterexampleTests
    {
        [Theory]
        [InlineData(typeof(Fixtures.Clock.DirectNow), false)]
        [InlineData(typeof(Fixtures.Clock.LambdaNow), false)]
        [InlineData(typeof(Fixtures.Clock.AsyncNow), false)]
        [InlineData(typeof(Fixtures.Clock.OffsetNow), false)]
        [InlineData(typeof(Fixtures.Clock.Today), false)]
        [InlineData(typeof(Fixtures.Clock.SystemProvider), false)]
        [InlineData(typeof(Fixtures.Clock.NewId), false)]
        [InlineData(typeof(Fixtures.Clock.StopwatchReader), false)]
        [InlineData(typeof(Fixtures.Clock.StopwatchType), false)]
        [InlineData(typeof(Fixtures.Clock.GenericStopwatch), false)]
        [InlineData(typeof(Fixtures.Clock.TickCount), false)]
        [InlineData(typeof(Fixtures.Clock.TickCount64), false)]
        [InlineData(typeof(Fixtures.Clock.PureValues), true)]
        [InlineData(typeof(Fixtures.Clock.PureLambda), true)]
        [InlineData(typeof(Fixtures.Clock.PureAsync), true)]
        public void Time_and_id_rule_rejects_side_effects_and_allows_supplied_values(Type type, bool expected)
        {
            var result = Types.InAssembly(typeof(GuardrailCounterexampleTests).Assembly).That()
                .HaveName(type.Name).Should().MeetCustomRule(new NoCurrentTimeRule()).GetResult();
            Assert.Equal(expected, result.IsSuccessful);
            if (!expected) Assert.Contains(type.FullName!, result.FailingTypeNames);
        }

        [Theory]
        [InlineData(typeof(Fixtures.SideEffects.FileReader), "System.IO", false)]
        [InlineData(typeof(Fixtures.SideEffects.ProcessCreator), "System.Diagnostics.Process", false)]
        [InlineData(typeof(Fixtures.SideEffects.NetworkCreator), "System.Net", false)]
        [InlineData(typeof(Fixtures.SideEffects.ConsoleWriter), "System.Console", false)]
        [InlineData(typeof(Fixtures.SideEffects.EnvironmentReader), "System.Environment", false)]
        [InlineData(typeof(Fixtures.SideEffects.AvaloniaConsumer), "Avalonia", false)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "System.IO", true)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "System.Diagnostics.Process", true)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "System.Net", true)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "System.Console", true)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "System.Environment", true)]
        [InlineData(typeof(Fixtures.SideEffects.PureData), "Avalonia", true)]
        public void Namespace_dependencies_reject_boundary_apis_and_allow_plain_data(Type type, string dependency, bool expected)
        {
            var result = Types.InAssembly(typeof(GuardrailCounterexampleTests).Assembly).That()
                .HaveName(type.Name).ShouldNot().HaveDependencyOn(dependency).GetResult();
            Assert.Equal(expected, result.IsSuccessful);
            if (!expected) Assert.Contains(type.FullName!, result.FailingTypeNames);
        }

        [Theory]
        [InlineData(typeof(Fixtures.Collections.ListRecord), false)]
        [InlineData(typeof(Fixtures.Collections.EnumerableRecord), false)]
        [InlineData(typeof(Fixtures.Collections.DictionaryRecord), false)]
        [InlineData(typeof(Fixtures.Collections.ArrayRecord), false)]
        [InlineData(typeof(Fixtures.Collections.ImmutableArrayRecord), false)]
        [InlineData(typeof(Fixtures.Collections.ReadOnlyListRecord), false)]
        [InlineData(typeof(Fixtures.Collections.NonGenericRecord), false)]
        [InlineData(typeof(Fixtures.Collections.WritableRecord), false)]
        [InlineData(typeof(Fixtures.Collections.WritableFieldRecord), false)]
        [InlineData(typeof(Fixtures.Collections.ReadonlyListFieldRecord), false)]
        [InlineData(typeof(Fixtures.Collections.ValueRecord), true)]
        [InlineData(typeof(Fixtures.Collections.StringRecord), true)]
        [InlineData(typeof(Fixtures.Collections.Codes), true)]
        public void Contract_whitelist_rejects_mutable_or_other_collections_and_allows_value_data(Type type, bool expected)
        {
            var failure = ContractDataRule.Failure(type);
            Assert.Equal(expected, failure is null);
            if (!expected) Assert.Contains(type.FullName!, failure!);
        }
    }
}

namespace MuxDeck.Architecture.Tests.Fixtures.Clock
{
    internal sealed class DirectNow { public DateTime Read() => DateTime.Now; }
    internal sealed class LambdaNow { public Func<DateTime> Read() => () => DateTime.Now; }
    internal sealed class AsyncNow { public async Task<DateTime> Read() { await Task.Yield(); return DateTime.Now; } }
    internal sealed class OffsetNow { public DateTimeOffset Read() => DateTimeOffset.UtcNow; }
    internal sealed class Today { public DateTime Read() => DateTime.Today; }
    internal sealed class SystemProvider { public TimeProvider Read() => TimeProvider.System; }
    internal sealed class NewId { public Guid Read() => Guid.NewGuid(); }
    internal sealed class StopwatchReader { public long Read() => System.Diagnostics.Stopwatch.GetTimestamp(); }
    internal sealed class StopwatchType { public Type Read() => typeof(System.Diagnostics.Stopwatch); }
    internal sealed class GenericStopwatch { public List<System.Diagnostics.Stopwatch> Values { get; } = []; }
    internal sealed class TickCount { public int Read() => Environment.TickCount; }
    internal sealed class TickCount64 { public long Read() => Environment.TickCount64; }
    internal sealed record PureValues(DateTime Time, Guid Id);
    internal sealed class PureLambda { public Func<DateTime> Read(DateTime time) => () => time; }
    internal sealed class PureAsync { public async Task<DateTime> Read(DateTime time) { await Task.Yield(); return time; } }
}

namespace MuxDeck.Architecture.Tests.Fixtures.SideEffects
{
    internal sealed class FileReader { public string Read(string path) => File.ReadAllText(path); }
    internal sealed class ProcessCreator { public object Create() => new System.Diagnostics.Process(); }
    internal sealed class NetworkCreator { public object Create() => new System.Net.Http.HttpClient(); }
    internal sealed class ConsoleWriter { public void Write(string value) => Console.WriteLine(value); }
    internal sealed class EnvironmentReader { public string Read() => Environment.MachineName; }
    internal sealed class AvaloniaConsumer { public Type Read() => typeof(Avalonia.Controls.Button); }
    internal sealed record PureData(string Path, int ExitCode);
}

namespace MuxDeck.Architecture.Tests.Fixtures.Collections
{
    internal sealed record ListRecord(List<int> Values);
    internal sealed record EnumerableRecord(IEnumerable<int> Values);
    internal sealed record DictionaryRecord(Dictionary<string, int> Values);
    internal sealed record ArrayRecord(int[] Values);
    internal sealed record ImmutableArrayRecord(ImmutableArray<int> Values);
    internal sealed record ReadOnlyListRecord(IReadOnlyList<int> Values);
    internal sealed record NonGenericRecord(IEnumerable Values);
    internal sealed record WritableRecord { public int Value { get; set; } }
    internal sealed record WritableFieldRecord { public int Value = 0; }
    internal sealed record ReadonlyListFieldRecord { public readonly List<int> Values = []; }
    internal sealed record ValueRecord(EquatableArray<int> Values);
    internal sealed record StringRecord(string Value);
    internal static class Codes { public const string Value = "CODE"; }
}
