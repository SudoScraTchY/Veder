using Domain.Entities;
using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IProviderSelector
{
    Task<(WeatherForecast Forecast, string ProviderId, bool FromCache)> ResolveWeatherAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct);
    Task<(AirQualityReading Reading, string ProviderId, bool FromCache)> ResolveAirQualityAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct);
}