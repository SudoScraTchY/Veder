using System.Collections.Concurrent;
using Domain.Entities;
using Domain.Entities.Interfaces;

namespace Infrastructure.Persistence.InMemory;

/// <summary>
/// Process-local implementation of <see cref="ISavedLocationRepository"/>.
/// It keeps the saved-location flow working end to end until the durable MongoDB
/// repository from the build plan (Phase 4, "Mongo repositories") replaces it.
/// </summary>
public sealed class InMemorySavedLocationRepository : ISavedLocationRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SavedLocation>> _byUser =
        new(StringComparer.Ordinal);

    public Task AddAsync(SavedLocation location, CancellationToken ct)
    {
        var forUser = _byUser.GetOrAdd(
            location.UserId,
            _ => new ConcurrentDictionary<string, SavedLocation>(StringComparer.Ordinal));

        forUser[location.Id] = location;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string userId, string locationId, CancellationToken ct)
    {
        if (_byUser.TryGetValue(userId, out var forUser))
        {
            forUser.TryRemove(locationId, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SavedLocation>> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        IReadOnlyList<SavedLocation> locations = _byUser.TryGetValue(userId, out var forUser)
            ? forUser.Values.ToList()
            : [];

        return Task.FromResult(locations);
    }

    public Task<SavedLocation?> GetByIdAsync(string userId, string locationId, CancellationToken ct)
    {
        SavedLocation? location =
            _byUser.TryGetValue(userId, out var forUser) && forUser.TryGetValue(locationId, out var found)
                ? found
                : null;

        return Task.FromResult(location);
    }
}
