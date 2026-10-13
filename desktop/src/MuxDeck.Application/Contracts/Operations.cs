using System.Collections.Immutable;

namespace MuxDeck.Contracts;

public enum OperationKind { SingleMux, MediaImport, ComponentInspection, ComponentInstall, ComponentUninstall }
public sealed record OperationState(bool IsShuttingDown, ImmutableArray<OperationKind> ActiveOperations);

public interface IOperationLease : IDisposable, IAsyncDisposable
{
    OperationKind Kind { get; }
}

public interface IOperationCoordinator
{
    OperationState State { get; }
    event EventHandler<OperationState>? StateChanged;
    ValueTask<IOperationLease?> TryAcquireAsync(OperationKind kind, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
