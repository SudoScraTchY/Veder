namespace Domain.Entities.Exceptions;

public sealed class ProviderRequestException(string providerId, string reason)
    : Exception($"Provider '{providerId}' rejected the request: {reason}")
{
    public string ProviderId { get; } = providerId;
    public string Reason { get; } = reason;
}
