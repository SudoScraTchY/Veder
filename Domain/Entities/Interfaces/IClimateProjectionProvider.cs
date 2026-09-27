using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IClimateProjectionProvider
{
    string ProviderId { get; }
    Task<WeatherSeries> GetClimateProjectionAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct);
}
