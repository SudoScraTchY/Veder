using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IFloodForecastProvider
{
    string ProviderId { get; }
    Task<WeatherSeries> GetFloodAsync(Coordinates location, CancellationToken ct);
}
