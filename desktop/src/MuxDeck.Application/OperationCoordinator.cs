using MuxDeck.Contracts;

namespace MuxDeck.Application;

/// <summary>替换旧组件 Gate 的内存租约；UI 接纳条件仍由迁移期调用方保持。</summary>
public sealed class OperationCoordinator : IOperationCoordinator
{
    private readonly object sync = new();
    private readonly List<Lease> leases = [];
    private readonly Queue<OperationState> notifications = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing, notifying;

    public OperationState State { get { lock (sync) return Snapshot(); } }
    public event EventHandler<OperationState>? StateChanged;

    public ValueTask<IOperationLease?> TryAcquireAsync(OperationKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Lease lease;
        lock (sync)
        {
            if (closing || IsComponentMutation(kind) && leases.Any(value => IsComponentMutation(value.Kind)))
                return ValueTask.FromResult<IOperationLease?>(null);
            lease = new(this, kind);
            leases.Add(lease);
            notifications.Enqueue(Snapshot());
        }
        Notify();
        return ValueTask.FromResult<IOperationLease?>(lease);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!closing)
            {
                closing = true;
                notifications.Enqueue(Snapshot());
            }
            if (leases.Count == 0) drained.TrySetResult();
        }
        Notify();
        // 取消仅取消等待；协调器不会重新开放，已有租约仍负责收尾。
        return drained.Task.WaitAsync(cancellationToken);
    }

    private static bool IsComponentMutation(OperationKind kind) =>
        kind is OperationKind.ComponentInstall or OperationKind.ComponentUninstall;
    private OperationState Snapshot() => new(closing, new(leases.Select(value => value.Kind)));
    private void Release(Lease lease)
    {
        lock (sync)
        {
            if (!leases.Remove(lease)) return;
            notifications.Enqueue(Snapshot());
            if (closing && leases.Count == 0) drained.TrySetResult();
        }
        Notify();
    }

    private void Notify()
    {
        lock (sync) { if (notifying) return; notifying = true; }
        while (true)
        {
            OperationState state;
            lock (sync)
            {
                if (notifications.Count == 0) { notifying = false; return; }
                state = notifications.Dequeue();
            }
            // 按变更顺序通知，订阅者异常不泄漏已经取得或释放的租约。
            foreach (var handler in StateChanged?.GetInvocationList() ?? [])
                try { ((EventHandler<OperationState>)handler)(this, state); }
                catch (Exception) { }
        }
    }

    private sealed class Lease(OperationCoordinator owner, OperationKind kind) : IOperationLease
    {
        private OperationCoordinator? coordinator = owner;
        public OperationKind Kind { get; } = kind;
        public void Dispose() => Interlocked.Exchange(ref coordinator, null)?.Release(this);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
