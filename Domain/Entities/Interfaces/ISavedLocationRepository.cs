using Domain.Entities;
using Domain.Entities.ValueObjects;

namespace Domain.Entities.Interfaces;

public interface ISavedLocationRepository
{
    Task AddAsync(SavedLocation location, CancellationToken ct);
    Task DeleteAsync(string userId, string locationId, CancellationToken ct);
    Task<IReadOnlyList<SavedLocation>> GetByUserIdAsync(string userId, CancellationToken ct);
    Task<SavedLocation?> GetByIdAsync(string userId, string locationId, CancellationToken ct);
}