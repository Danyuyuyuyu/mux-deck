using System.Reflection;
using MuxDeck.Application;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class CompositionAssemblyTests
{
    [Fact]
    public void Production_single_service_receives_all_shared_ports_explicitly()
    {
        var service = MuxDeck.Desktop.Composition.AppComposition.CreateSingleService(new Workspace("C:/mux-deck-test"), new NoopRunner());
        Assert.IsType<MuxDeck.Infrastructure.SystemPathFacts>(service.PathFacts);
        Assert.Same(service.PathFacts, service.CollectFacts.Target);
        var import = Assert.IsType<PrepareImportUseCase>(service.Import);
        var reader = typeof(PrepareImportUseCase).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(MuxDeck.Application.Ports.IMuxInputReader)).GetValue(import);
        var paths = typeof(PrepareImportUseCase).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(MuxDeck.Application.Ports.IPathFacts)).GetValue(import);
        Assert.Same(service.InputReader, reader);
        Assert.Same(service.PathFacts, paths);
    }

    private sealed class NoopRunner : IMuxToolRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
