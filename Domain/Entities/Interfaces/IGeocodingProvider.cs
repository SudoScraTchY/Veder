using System.Threading;

namespace Domain.Entities.Interfaces;

public interface IGeocodingProvider
{
    string ProviderId { get; }
    Task<IReadOnlyList<GeoLocation>> SearchAsync(string name, int count, string? language, CancellationToken ct);
    Task<GeoLocation?> GetByIdAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<GeoLocation>> GetByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct);
}
