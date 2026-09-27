namespace Domain.Caching;

/// <summary>How a cache read was satisfied. Recorded as a counter, never as one row per hit.</summary>
public enum CacheOutcome
{
    /// <summary>Served from a live entry.</summary>
    Hit,

    /// <summary>Nothing usable was cached; the factory ran.</summary>
    Miss,

    /// <summary>Waited behind another caller for the same key and reused its result instead of calling upstream.</summary>
    Coalesced,

    /// <summary>The entry had expired but was still inside its grace window and the upstream call failed, so a stale value was served rather than an error.</summary>
    StaleOnProviderFailure,

    /// <summary>The entry was not usable and the upstream call failed with nothing stale to fall back on.</summary>
    Failed,

    /// <summary>The caller asked to skip the cache.</summary>
    Bypassed
}

/// <summary>What the factory produced: the payload plus the provider that actually served it.</summary>
public sealed record CacheProduced<T>(T Value, string SourceProviderId);

/// <summary>The result of a cache read, including provenance and freshness for the response envelope.</summary>
public sealed record CacheReadResult<T>(
    T Value,
    string SourceProviderId,
    CacheOutcome Outcome,
    TimeSpan Age,
    bool IsStale)
{
    public bool ServedFromCache => Outcome is CacheOutcome.Hit or CacheOutcome.Coalesced or CacheOutcome.StaleOnProviderFailure;

    /// <summary>True when the value is past its freshness window and should be labelled as degraded to the caller.</summary>
    public bool IsDegraded => Outcome == CacheOutcome.StaleOnProviderFailure;
}

/// <summary>Per-read behaviour.</summary>
public sealed record CacheReadOptions
{
    public static CacheReadOptions Default { get; } = new();

    /// <summary>Skip the read path entirely (admin refresh, cache-busting queries).</summary>
    public bool BypassCache { get; init; }

    /// <summary>How long past expiry a value may still be served if the provider call fails.</summary>
    public TimeSpan StaleGrace { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>False disables stale-on-failure for this read.</summary>
    public bool AllowStaleOnFailure { get; init; } = true;
}

/// <summary>
/// Counter snapshot for the cache. Deliberately aggregated: recording every hit as a ledger row turns
/// telemetry into a critical-path dependency (at 10k rps with a 95% hit rate that is 9.5k writes/sec).
/// </summary>
public sealed record CacheStatistics(IReadOnlyDictionary<CacheOutcome, long> Counts, long UpstreamCalls, long UpstreamFailures)
{
    public long TotalReads => Counts.Values.Sum();

    public long For(CacheOutcome outcome) => Counts.TryGetValue(outcome, out var count) ? count : 0;

    public double HitRatio
    {
        get
        {
            var reads = TotalReads;
            return reads == 0 ? 0 : (double)(For(CacheOutcome.Hit) + For(CacheOutcome.Coalesced)) / reads;
        }
    }
}

/// <summary>
/// The cache the selector reads through. Key derivation belongs to <see cref="WeatherCacheKey"/>; this
/// interface only governs read-through behaviour, so the backing store can be memory today and Redis
/// or Garnet later without touching callers.
/// </summary>
public interface IWeatherCacheStore
{
    /// <summary>
    /// Reads <paramref name="key"/>, calling <paramref name="factory"/> at most once per key per expiry
    /// window even under concurrent load. A provider failure inside the factory falls back to a stale
    /// value inside the grace window instead of surfacing an error.
    /// </summary>
    Task<CacheReadResult<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<CacheProduced<T>>> factory,
        TimeSpan ttl,
        CacheReadOptions? options,
        CancellationToken ct);

    /// <summary>Removes one entry; returns true when something was removed.</summary>
    bool Remove(string key);

    /// <summary>Removes every entry whose key starts with the prefix, for admin invalidation.</summary>
    int RemoveByPrefix(string prefix);

    /// <summary>Number of live entries.</summary>
    int Count { get; }

    CacheStatistics Snapshot();
}
