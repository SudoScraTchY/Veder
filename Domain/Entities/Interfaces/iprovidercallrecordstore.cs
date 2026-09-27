using Domain.Entities;

namespace Domain.Entities.Interfaces;

/// <summary>Append-only store of provider interactions.</summary>
public interface IProviderCallRecordStore
{
    Task AppendAsync(ProviderCallRecord record, CancellationToken ct);

    /// <summary>Most recent records across all providers, newest first.</summary>
    Task<IReadOnlyList<ProviderCallRecord>> GetRecentAsync(int count, CancellationToken ct);

    /// <summary>Most recent records for one provider, newest first.</summary>
    Task<IReadOnlyList<ProviderCallRecord>> GetByProviderAsync(string providerId, int count, CancellationToken ct);

    Task<long> CountAsync(CancellationToken ct);
}

/// <summary>Last-success / last-failure view per provider.</summary>
public interface IProviderDiagnosticsStore
{
    Task<ProviderDiagnostics?> GetAsync(string providerId, CancellationToken ct);

    Task<IReadOnlyList<ProviderDiagnostics>> GetAllAsync(CancellationToken ct);

    Task RecordSuccessAsync(string providerId, DateTimeOffset at, string operation, CancellationToken ct);

    Task RecordFailureAsync(string providerId, DateTimeOffset at, string operation, string outcome, string message, CancellationToken ct);
}
