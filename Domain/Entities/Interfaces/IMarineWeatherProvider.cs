using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IMarineWeatherProvider
{
    string ProviderId { get; }
    Task<WeatherSeries> GetMarineAsync(Coordinates location, CancellationToken ct);
}
