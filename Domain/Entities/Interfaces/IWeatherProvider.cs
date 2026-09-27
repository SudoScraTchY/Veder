using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IWeatherProvider
{
    string ProviderId { get; }
    Task<WeatherForecast> GetForecastAsync(Coordinates location, CancellationToken ct);
}