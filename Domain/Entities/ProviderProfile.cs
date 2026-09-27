using Domain.Entities.Enumerations;

namespace Domain.Entities;

public sealed class ProviderProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ProviderCapability Capabilities { get; init; }
    public required int Priority { get; init; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? ApiKeyExpiresAt { get; set; }
    public int? DailyQuota { get; init; }
    public int UsedQuota { get; private set; } = 0;
    public DateTimeOffset? LastHealthCheck { get; private set; }
    public bool IsHealthy { get; private set; } = true;

    public void MarkHealthy()
    {
        IsHealthy = true;
        LastHealthCheck = DateTimeOffset.UtcNow;
    }

    public void MarkUnhealthy()
    {
        IsHealthy = false;
        LastHealthCheck = DateTimeOffset.UtcNow;
    }

    public void IncrementQuota()
    {
        UsedQuota++;
    }

    public void ResetQuota()
    {
        UsedQuota = 0;
    }

    public bool HasQuotaRemaining => !DailyQuota.HasValue || UsedQuota < DailyQuota.Value;
}