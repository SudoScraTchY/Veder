using Domain.Entities.Enumerations;
using Domain.Entities.Exceptions;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Infrastructure.Cache;
using Infrastructure.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Veder.Tests;

public sealed class ProviderSelectorTests
{
    private static readonly Coordinates London = Coordinates.FromDegrees(51.5, -0.12);

    private static DistributedCacheService NewCache()
        => new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    private static ProviderSelector NewSelector(
        ProviderRegistry registry,
        ICacheService cache,
        IEnumerable<IWeatherProvider> weather,
        IEnumerable<IAirQualityProvider>? airQuality = null)
        => new(registry, cache, new CacheKeyBuilder(), weather, airQuality ?? []);

    [Fact]
    public async Task ExplicitProvider_CacheHit_DoesNotCallProvider()
    {
        var cache = NewCache();
        var keys = new CacheKeyBuilder();
        await cache.SetAsync(keys.ForWeather("open-meteo", London), Sample.Forecast(London, "open-meteo"), TimeSpan.FromMinutes(5), CancellationToken.None);

        var provider = new FakeWeatherProvider("open-meteo");
        var selector = NewSelector(new ProviderRegistry(), cache, [provider]);

        var (forecast, providerId, fromCache) = await selector.ResolveWeatherAsync(London, "open-meteo", CancellationToken.None);

        Assert.True(fromCache);
        Assert.Equal("open-meteo", providerId);
        Assert.Equal(0, provider.Calls);
        Assert.Equal("open-meteo", forecast.ProviderId);
    }

    [Fact]
    public async Task ExplicitProvider_Miss_FetchesThenServesFromCache()
    {
        var cache = NewCache();
        var provider = new FakeWeatherProvider("open-meteo");
        var selector = NewSelector(new ProviderRegistry(), cache, [provider]);

        var (_, _, firstFromCache) = await selector.ResolveWeatherAsync(London, "open-meteo", CancellationToken.None);
        var (_, _, secondFromCache) = await selector.ResolveWeatherAsync(London, "open-meteo", CancellationToken.None);

        Assert.False(firstFromCache);
        Assert.True(secondFromCache);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ExplicitProvider_Failure_ThrowsAndNeverSubstitutes()
    {
        var openMeteo = new FakeWeatherProvider("open-meteo", new HttpRequestException("boom"));
        var openWeatherMap = new FakeWeatherProvider("openweathermap");
        var registry = new ProviderRegistry();
        var selector = NewSelector(registry, NewCache(), [openMeteo, openWeatherMap]);

        await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => selector.ResolveWeatherAsync(London, "open-meteo", CancellationToken.None));

        Assert.Equal(1, openMeteo.Calls);
        Assert.Equal(0, openWeatherMap.Calls);
        Assert.False(registry.Get("open-meteo")!.IsHealthy);
    }

    [Fact]
    public async Task AutoMode_ServesHighestPriorityHealthyProvider()
    {
        var openMeteo = new FakeWeatherProvider("open-meteo");
        var openWeatherMap = new FakeWeatherProvider("openweathermap");
        var selector = NewSelector(new ProviderRegistry(), NewCache(), [openMeteo, openWeatherMap]);

        var (_, providerId, fromCache) = await selector.ResolveWeatherAsync(London, null, CancellationToken.None);

        Assert.Equal("open-meteo", providerId);
        Assert.False(fromCache);
        Assert.Equal(1, openMeteo.Calls);
        Assert.Equal(0, openWeatherMap.Calls);
    }

    [Fact]
    public async Task AutoMode_FirstProviderFails_FallsBackAndMarksItUnhealthy()
    {
        var openMeteo = new FakeWeatherProvider("open-meteo", new HttpRequestException("boom"));
        var openWeatherMap = new FakeWeatherProvider("openweathermap");
        var registry = new ProviderRegistry();
        var selector = NewSelector(registry, NewCache(), [openMeteo, openWeatherMap]);

        var (_, providerId, _) = await selector.ResolveWeatherAsync(London, null, CancellationToken.None);

        Assert.Equal("openweathermap", providerId);
        Assert.False(registry.Get("open-meteo")!.IsHealthy);
        Assert.True(registry.Get("openweathermap")!.IsHealthy);
    }

    [Fact]
    public async Task AutoMode_AlreadyUnhealthyProvider_IsSkippedEntirely()
    {
        var openMeteo = new FakeWeatherProvider("open-meteo");
        var openWeatherMap = new FakeWeatherProvider("openweathermap");
        var registry = new ProviderRegistry();
        registry.MarkUnhealthy("open-meteo");
        var selector = NewSelector(registry, NewCache(), [openMeteo, openWeatherMap]);

        var (_, providerId, _) = await selector.ResolveWeatherAsync(London, null, CancellationToken.None);

        Assert.Equal("openweathermap", providerId);
        Assert.Equal(0, openMeteo.Calls);
    }

    [Fact]
    public async Task AutoMode_AllProvidersFail_ThrowsAllProvidersUnavailable()
    {
        var openMeteo = new FakeWeatherProvider("open-meteo", new HttpRequestException("boom"));
        var openWeatherMap = new FakeWeatherProvider("openweathermap", new HttpRequestException("boom"));
        var selector = NewSelector(new ProviderRegistry(), NewCache(), [openMeteo, openWeatherMap]);

        await Assert.ThrowsAsync<AllProvidersUnavailableException>(
            () => selector.ResolveWeatherAsync(London, null, CancellationToken.None));
    }

    [Fact]
    public async Task AutoMode_SuccessIsCachedUnderTheServingProvider()
    {
        var cache = NewCache();
        var keys = new CacheKeyBuilder();
        var openMeteo = new FakeWeatherProvider("open-meteo");
        var selector = NewSelector(new ProviderRegistry(), cache, [openMeteo]);

        await selector.ResolveWeatherAsync(London, null, CancellationToken.None);
        var cached = await cache.TryGetAsync<Domain.Entities.WeatherForecast>(keys.ForWeather("open-meteo", London), CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal("open-meteo", cached!.ProviderId);
    }

    [Fact]
    public async Task AirQuality_ResolvesThroughAirQualityProviders()
    {
        var openAq = new FakeAirQualityProvider("openaq");
        var selector = NewSelector(new ProviderRegistry(), NewCache(), [], [openAq]);

        var (reading, providerId, fromCache) = await selector.ResolveAirQualityAsync(London, null, CancellationToken.None);

        Assert.Equal("openaq", providerId);
        Assert.False(fromCache);
        Assert.Equal(42, reading.Index.Value);
        Assert.Equal(1, openAq.Calls);
    }

    [Fact]
    public async Task WeatherCapabilityRequest_DoesNotConsiderAirQualityOnlyProviders()
    {
        var registry = new ProviderRegistry();

        var weather = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);

        Assert.DoesNotContain(weather, p => p.Id == "openaq");
        Assert.DoesNotContain(weather, p => p.Id == "waqi");
        Assert.Contains(weather, p => p.Id == "open-meteo");
    }
}
