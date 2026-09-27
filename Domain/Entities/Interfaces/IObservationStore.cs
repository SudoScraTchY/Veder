using Domain.Entities;

namespace Domain.Entities.Interfaces;

/// <summary>
/// Store for the modelled observations we sync from providers. Upserts are keyed by
/// <see cref="ObservationRecord.Id"/>, so a replayed reading never creates a duplicate row.
/// </summary>
public interface IObservationStore
{
    Task<ObservationRecord> UpsertAsync(ObservationRecord record, CancellationToken ct);

    Task<IReadOnlyList<ObservationRecord>> GetRecentAsync(string? kind, int count, CancellationToken ct);

    Task<ObservationRecord?> GetByIdAsync(string id, CancellationToken ct);

    Task<long> CountAsync(CancellationToken ct);
}
