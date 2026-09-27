using Domain.Entities;
using Domain.Entities.ValueObjects;

namespace Domain.Entities.Interfaces;

/// <summary>
/// Optional capability: providers that can return a full time series rather than just the current
/// observation. Kept separate from <see cref="IWeatherProvider"/> so a provider that only offers
/// current conditions stays a first-class citizen.
/// </summary>
public interface IWeatherSeriesProvider
{
    string ProviderId { get; }

    Task<WeatherSeries> GetForecastSeriesAsync(Coordinates location, CancellationToken ct);
}
