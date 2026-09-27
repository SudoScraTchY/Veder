namespace Domain.Entities.Exceptions;

public sealed class ProviderRateLimitedException(string providerId, TimeSpan? retryAfter)
    : Exception($"Provider '{providerId}' rate limited the request.")
{
    public string ProviderId { get; } = providerId;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
