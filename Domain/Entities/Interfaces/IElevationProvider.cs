using Domain.Entities.ValueObjects;
using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IElevationProvider
{
    string ProviderId { get; }
    Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<Coordinates> locations, CancellationToken ct);
}
