using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IHistoricalWeatherProvider
{
    string ProviderId { get; }
    Task<WeatherSeries> GetArchiveAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<WeatherSeries> GetHistoricalForecastAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct);
}
