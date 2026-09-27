using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.Exceptions;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers;

public sealed class ProviderSelector(
    IProviderRegistry registry,
    ICacheService cache,
    ICacheKeyBuilder keys,
    IEnumerable<IWeatherProvider> weatherProviders,
    IEnumerable<IAirQualityProvider> airQualityProviders) : IProviderSelector
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private const ProviderCapability WeatherCapabilities =
        ProviderCapability.CurrentWeather | ProviderCapability.Forecast;

    private const ProviderCapability AirQualityCapabilities = ProviderCapability.AirQuality;

    public async Task<(WeatherForecast Forecast, string ProviderId, bool FromCache)> ResolveWeatherAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct)
    {
        if (requestedProviderId is not null)
        {
            var requestedKey = keys.ForWeather(requestedProviderId, location);
            if (await cache.TryGetAsync<WeatherForecast>(requestedKey, ct) is { } cached)
                return (cached, requestedProviderId, true);

            var requested = weatherProviders.FirstOrDefault(p => p.ProviderId == requestedProviderId)
                ?? throw new ProviderUnavailableException(requestedProviderId);

            try
            {
                var fresh = await requested.GetForecastAsync(location, ct);
                await cache.SetAsync(requestedKey, fresh, CacheTtl, ct);
                registry.RecordUsage(requestedProviderId);
                registry.MarkHealthy(requestedProviderId);
                return (fresh, requestedProviderId, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registry.MarkUnhealthy(requestedProviderId);
                throw new ProviderUnavailableException(requestedProviderId);
            }
        }

        var candidates = registry.GetHealthyByPriority(WeatherCapabilities);

        foreach (var candidate in candidates)
        {
            if (await cache.TryGetAsync<WeatherForecast>(keys.ForWeather(candidate.Id, location), ct) is { } cached)
                return (cached, candidate.Id, true);
        }

        foreach (var candidate in candidates)
        {
            var provider = weatherProviders.FirstOrDefault(p => p.ProviderId == candidate.Id);
            if (provider is null) continue;

            try
            {
                var fresh = await provider.GetForecastAsync(location, ct);
                await cache.SetAsync(keys.ForWeather(candidate.Id, location), fresh, CacheTtl, ct);
                registry.RecordUsage(candidate.Id);
                registry.MarkHealthy(candidate.Id);
                return (fresh, candidate.Id, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registry.MarkUnhealthy(candidate.Id);
            }
        }

        throw new AllProvidersUnavailableException();
    }

    public async Task<(AirQualityReading Reading, string ProviderId, bool FromCache)> ResolveAirQualityAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct)
    {
        if (requestedProviderId is not null)
        {
            var requestedKey = keys.ForAirQuality(requestedProviderId, location);
            if (await cache.TryGetAsync<AirQualityReading>(requestedKey, ct) is { } cached)
                return (cached, requestedProviderId, true);

            var requested = airQualityProviders.FirstOrDefault(p => p.ProviderId == requestedProviderId)
                ?? throw new ProviderUnavailableException(requestedProviderId);

            try
            {
                var fresh = await requested.GetCurrentAsync(location, ct);
                await cache.SetAsync(requestedKey, fresh, CacheTtl, ct);
                registry.RecordUsage(requestedProviderId);
                registry.MarkHealthy(requestedProviderId);
                return (fresh, requestedProviderId, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registry.MarkUnhealthy(requestedProviderId);
                throw new ProviderUnavailableException(requestedProviderId);
            }
        }

        var candidates = registry.GetHealthyByPriority(AirQualityCapabilities);

        foreach (var candidate in candidates)
        {
            if (await cache.TryGetAsync<AirQualityReading>(keys.ForAirQuality(candidate.Id, location), ct) is { } cached)
                return (cached, candidate.Id, true);
        }

        foreach (var candidate in candidates)
        {
            var provider = airQualityProviders.FirstOrDefault(p => p.ProviderId == candidate.Id);
            if (provider is null) continue;

            try
            {
                var fresh = await provider.GetCurrentAsync(location, ct);
                await cache.SetAsync(keys.ForAirQuality(candidate.Id, location), fresh, CacheTtl, ct);
                registry.RecordUsage(candidate.Id);
                registry.MarkHealthy(candidate.Id);
                return (fresh, candidate.Id, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registry.MarkUnhealthy(candidate.Id);
            }
        }

        throw new AllProvidersUnavailableException();
    }
}
