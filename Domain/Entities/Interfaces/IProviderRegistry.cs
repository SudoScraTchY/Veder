using Domain.Entities;
using Domain.Entities.Enumerations;

namespace Domain.Entities.Interfaces;

public interface IProviderRegistry
{
    IReadOnlyList<ProviderProfile> GetAll();
    ProviderProfile? Get(string providerId);
    IReadOnlyList<ProviderProfile> GetHealthyByPriority(ProviderCapability capability);

    Task<bool> SetEnabledAsync(string providerId, bool enabled, CancellationToken ct);
    Task<bool> SetPriorityAsync(string providerId, int priority, CancellationToken ct);
    void MarkHealthy(string providerId);
    void MarkUnhealthy(string providerId);
    void RecordUsage(string providerId);
}