using Domain.Caching;
using Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;

namespace Veder.Tests;

/// <summary>
/// Read-through behaviour the design review demanded: single-flight coalescing, stale-on-failure,
/// per-type TTL expiry, prefix invalidation and aggregated counters.
/// </summary>
public sealed class WeatherCacheStoreTests
{
    private static InMemoryWeatherCacheStore NewStore() => new(NullLogger<InMemoryWeatherCacheStore>.Instance);

    private static Func<CancellationToken, Task<CacheProduced<string>>> Counting(string value, string provider, int[] calls, TimeSpan? delay = null) =>
        async ct =>
        {
            Interlocked.Increment(ref calls[0]);
            if (delay is { } wait)
            {
                await Task.Delay(wait, ct);
            }

            return new CacheProduced<string>(value, provider);
        };

    [Fact]
    public async Task SecondReadWithinTtlIsAServedHit()
    {
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("22C", "open-meteo", calls);

        var first = await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        var second = await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, first.Outcome);
        Assert.Equal(CacheOutcome.Hit, second.Outcome);
        Assert.True(second.ServedFromCache);
        Assert.False(second.IsDegraded);
        Assert.Equal(1, calls[0]);
        Assert.Equal("open-meteo", second.SourceProviderId);
    }

    [Fact]
    public async Task ConcurrentReadersCollapseIntoOneUpstreamCall()
    {
        // The thundering-herd case: 200 simultaneous readers of one expired key must not fire 200 calls.
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("22C", "open-meteo", calls, TimeSpan.FromMilliseconds(150));

        var readers = Enumerable.Range(0, 200)
            .Select(_ => store.GetOrCreateAsync("hot-key", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(readers);

        Assert.Equal(1, calls[0]);
        Assert.Single(results.Select(r => r.Outcome).Distinct().Where(o => o == CacheOutcome.Miss));
        Assert.Equal(199, results.Count(r => r.Outcome == CacheOutcome.Coalesced));
        Assert.Equal(1, store.Snapshot().UpstreamCalls);
    }

    [Fact]
    public async Task ExpiredEntryIsRefreshed()
    {
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("22C", "open-meteo", calls);

        await store.GetOrCreateAsync("k", factory, TimeSpan.FromMilliseconds(60), null, CancellationToken.None);
        await Task.Delay(120);
        var refreshed = await store.GetOrCreateAsync("k", factory, TimeSpan.FromMilliseconds(60), null, CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, refreshed.Outcome);
        Assert.Equal(2, calls[0]);
    }

    [Fact]
    public async Task ProviderFailureInsideTheGraceWindowServesStaleRatherThanFailing()
    {
        var store = NewStore();
        await store.GetOrCreateAsync("k", Counting("22C", "open-meteo", new int[1]), TimeSpan.FromMilliseconds(40), null, CancellationToken.None);
        await Task.Delay(80);

        var failing = await store.GetOrCreateAsync<string>(
            "k",
            _ => throw new HttpRequestException("provider down"),
            TimeSpan.FromMilliseconds(40),
            new CacheReadOptions { StaleGrace = TimeSpan.FromMinutes(10) },
            CancellationToken.None);

        Assert.Equal(CacheOutcome.StaleOnProviderFailure, failing.Outcome);
        Assert.True(failing.IsStale);
        Assert.True(failing.IsDegraded);
        Assert.Equal("22C", failing.Value);
        Assert.Equal("open-meteo", failing.SourceProviderId);
        Assert.Equal(1, store.Snapshot().UpstreamFailures);
    }

    [Fact]
    public async Task ProviderFailureOutsideTheGraceWindowPropagates()
    {
        var store = NewStore();
        await store.GetOrCreateAsync("k", Counting("22C", "open-meteo", new int[1]), TimeSpan.FromMilliseconds(20), null, CancellationToken.None);
        await Task.Delay(50);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.GetOrCreateAsync<string>(
            "k",
            _ => throw new HttpRequestException("provider down"),
            TimeSpan.FromMilliseconds(20),
            new CacheReadOptions { StaleGrace = TimeSpan.Zero },
            CancellationToken.None));
    }

    [Fact]
    public async Task BypassSkipsBothTheReadAndTheWrite()
    {
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("22C", "open-meteo", calls);

        await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), new CacheReadOptions { BypassCache = true }, CancellationToken.None);

        Assert.Equal(2, calls[0]);
        Assert.Equal(1, store.Snapshot().For(CacheOutcome.Bypassed));
    }

    [Fact]
    public async Task EntriesAreIsolatedPerKey()
    {
        var store = NewStore();
        var first = new int[1];
        var second = new int[1];

        await store.GetOrCreateAsync("v1|CurrentConditions|open-meteo|a", Counting("22C", "open-meteo", first), TimeSpan.FromMinutes(5), null, CancellationToken.None);
        await store.GetOrCreateAsync("v1|CurrentConditions|open-meteo|b", Counting("24C", "open-meteo", second), TimeSpan.FromMinutes(5), null, CancellationToken.None);

        Assert.Equal(1, first[0]);
        Assert.Equal(1, second[0]);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public async Task PrefixInvalidationRemovesOnlyMatchingEntries()
    {
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("x", "open-meteo", calls);

        await store.GetOrCreateAsync("v1|CurrentConditions|open-meteo|a", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        await store.GetOrCreateAsync("v1|CurrentConditions|open-meteo|b", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        await store.GetOrCreateAsync("v1|AirQuality|open-meteo|c", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);

        var removed = store.RemoveByPrefix(WeatherCacheKey.PrefixFor(WeatherDataType.CurrentConditions, "open-meteo"));

        Assert.Equal(2, removed);
        Assert.Equal(1, store.Count);
        Assert.True(store.Remove("v1|AirQuality|open-meteo|c"));
        Assert.False(store.Remove("v1|AirQuality|open-meteo|c"));
    }

    [Fact]
    public async Task TelemetryIsAggregatedCountersRatherThanOneRowPerHit()
    {
        var store = NewStore();
        var calls = new int[1];
        var factory = Counting("22C", "open-meteo", calls);

        await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        for (var i = 0; i < 50; i++)
        {
            await store.GetOrCreateAsync("k", factory, TimeSpan.FromMinutes(5), null, CancellationToken.None);
        }

        var snapshot = store.Snapshot();

        Assert.Equal(51, snapshot.TotalReads);
        Assert.Equal(1, snapshot.For(CacheOutcome.Miss));
        Assert.Equal(50, snapshot.For(CacheOutcome.Hit));
        Assert.Equal(1, snapshot.UpstreamCalls);
        Assert.True(snapshot.HitRatio > 0.97, $"expected a >0.97 hit ratio, got {snapshot.HitRatio:F3}");
    }

    [Fact]
    public async Task DifferentTypesGetDifferentLifetimes()
    {
        var store = NewStore();
        var current = new int[1];
        var forecast = new int[1];

        await store.GetOrCreateAsync("current", Counting("22C", "open-meteo", current), CacheTtlPolicy.For(WeatherDataType.CurrentConditions), null, CancellationToken.None);
        await store.GetOrCreateAsync("forecast", Counting("22C", "open-meteo", forecast), CacheTtlPolicy.For(WeatherDataType.Forecast), null, CancellationToken.None);

        // A 300 ms wait outlives neither, but proves the policy feeds the store rather than a constant.
        await Task.Delay(300);
        await store.GetOrCreateAsync("current", Counting("22C", "open-meteo", current), CacheTtlPolicy.For(WeatherDataType.CurrentConditions), null, CancellationToken.None);
        await store.GetOrCreateAsync("forecast", Counting("22C", "open-meteo", forecast), CacheTtlPolicy.For(WeatherDataType.Forecast), null, CancellationToken.None);

        Assert.Equal(1, current[0]);
        Assert.Equal(1, forecast[0]);
    }
}
