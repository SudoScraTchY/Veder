namespace Domain.Entities.Exceptions;

public sealed class ProviderUnavailableException(string providerId)
    : Exception($"Provider '{providerId}' is unavailable.")
{
    public string ProviderId { get; } = providerId;
}
