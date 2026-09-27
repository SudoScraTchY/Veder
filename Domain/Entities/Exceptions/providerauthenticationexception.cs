namespace Domain.Entities.Exceptions;

/// <summary>
/// Raised when a provider rejects our credential (HTTP 401/403 or an explicit
/// "invalid api key" reason). Distinct from <see cref="ProviderUnavailableException"/> so an
/// operator can tell "our key is wrong" from "the provider is down".
/// </summary>
public sealed class ProviderAuthenticationException(string providerId, string detail)
    : Exception($"Provider '{providerId}' rejected the configured credential: {detail}")
{
    public string ProviderId { get; } = providerId;

    public string Detail { get; } = detail;
}
