using System.Collections.Concurrent;
using Domain.Caching;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching;

/// <summary>
/// In-process read-through cache. Correctness properties that matter and are covered by tests:
/// concurrent readers of one key collapse into a single upstream call (single-flight); an upstream
/// failure inside the grace window serves the previous value instead of an error; and telemetry is
/// aggregated counters rather than a write per hit.
/// </summary>
public sealed class InMemoryWeatherCacheStore(ILogger<InMemoryWeatherCacheStore> logger, TimeProvider? timeProvider = null)
    : IWeatherCacheStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<CacheOutcome, long> _counts = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _upstreamCalls;
    private long _upstreamFailures;

    private sealed record Entry(object Value, string SourceProviderId, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);

    public int Count => _entries.Count;

    public async Task<CacheReadResult<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<CacheProduced<T>>> factory,
        TimeSpan ttl,
        CacheReadOptions? options,
        CancellationToken ct)
    {
        var readOptions = options ?? CacheReadOptions.Default;
        var now = _time.GetUtcNow();

        if (!readOptions.BypassCache && _entries.TryGetValue(key, out var existing) && existing.Value is T cached)
        {
            if (now < existing.ExpiresAt)
            {
                Record(CacheOutcome.Hit);
                return new CacheReadResult<T>(cached, existing.SourceProviderId, CacheOutcome.Hit, now - existing.FetchedAt, false);
            }
        }
        else if (readOptions.BypassCache)
        {
            Record(CacheOutcome.Bypassed);
        }

        // Single-flight: everyone for this key queues behind one leader, so an expiring hot entry cannot
        // fire N upstream calls at once.
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            now = _time.GetUtcNow();

            if (!readOptions.BypassCache && _entries.TryGetValue(key, out var winner) && winner.Value is T winnerValue && now < winner.ExpiresAt)
            {
                // The leader already refreshed it while we waited.
                Record(CacheOutcome.Coalesced);
                return new CacheReadResult<T>(winnerValue, winner.SourceProviderId, CacheOutcome.Coalesced, now - winner.FetchedAt, false);
            }

            Record(CacheOutcome.Miss);
            Interlocked.Increment(ref _upstreamCalls);

            try
            {
                var produced = await factory(ct);
                var fetchedAt = _time.GetUtcNow();
                _entries[key] = new Entry(produced.Value!, produced.SourceProviderId, fetchedAt, fetchedAt + ttl);

                return new CacheReadResult<T>(produced.Value, produced.SourceProviderId, CacheOutcome.Miss, TimeSpan.Zero, false);
            }
            catch (Exception ex) when (readOptions.AllowStaleOnFailure && !ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _upstreamFailures);
                Record(CacheOutcome.Failed);

                // Stale-on-failure: a provider outage is exactly when an outdated value beats an error.
                if (_entries.TryGetValue(key, out var stale) &&
                    stale.Value is T staleValue &&
                    _time.GetUtcNow() < stale.ExpiresAt + readOptions.StaleGrace)
                {
                    Record(CacheOutcome.StaleOnProviderFailure);
                    logger.LogWarning(ex,
                        "Provider call for {Key} failed; serving a stale value from {Provider} ({AgeSeconds:F0}s old)",
                        key, stale.SourceProviderId, (_time.GetUtcNow() - stale.FetchedAt).TotalSeconds);

                    return new CacheReadResult<T>(staleValue, stale.SourceProviderId, CacheOutcome.StaleOnProviderFailure, _time.GetUtcNow() - stale.FetchedAt, true);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public bool Remove(string key) => _entries.TryRemove(key, out _);

    public int RemoveByPrefix(string prefix)
    {
        var removed = 0;

        foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (_entries.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    public CacheStatistics Snapshot() => new(
        new Dictionary<CacheOutcome, long>(_counts),
        Interlocked.Read(ref _upstreamCalls),
        Interlocked.Read(ref _upstreamFailures));

    private void Record(CacheOutcome outcome) => _counts.AddOrUpdate(outcome, 1, (_, current) => current + 1);
}
