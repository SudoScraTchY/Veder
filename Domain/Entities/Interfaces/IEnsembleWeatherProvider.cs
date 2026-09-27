using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IEnsembleWeatherProvider
{
    string ProviderId { get; }
    Task<WeatherSeries> GetEnsembleAsync(Coordinates location, CancellationToken ct);
}
