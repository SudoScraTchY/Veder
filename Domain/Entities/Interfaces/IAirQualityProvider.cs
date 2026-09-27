using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IAirQualityProvider
{
    string ProviderId { get; }
    Task<AirQualityReading> GetCurrentAsync(Coordinates location, CancellationToken ct);
}