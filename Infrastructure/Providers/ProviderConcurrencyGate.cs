namespace Infrastructure.Providers;

/// <summary>
/// Bounds how many provider calls run at once, so <c>OpenMeteo:MaxConcurrentRequests</c> is enforced
/// rather than merely documented. Callers wait their turn instead of overwhelming a free-tier quota.
/// </summary>
public sealed class ProviderConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _gate;

    public ProviderConcurrencyGate(int maxConcurrent) => _gate = new SemaphoreSlim(Math.Clamp(maxConcurrent, 1, 32), Math.Clamp(maxConcurrent, 1, 32));

    public int MaxConcurrent { get; private init; }

    public int CurrentlyWaiting => _gate.CurrentCount;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Lease(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
