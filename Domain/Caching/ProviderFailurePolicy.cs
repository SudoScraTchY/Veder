using Domain.Entities.Exceptions;

namespace Domain.Caching;

/// <summary>Why a provider call failed. The kind decides both health accounting and whether we may substitute.</summary>
public enum ProviderFailureKind
{
    None,
    /// <summary>The request itself was wrong (400/422, unknown variable, range violation). Our fault, not the provider's.</summary>
    Rejection,
    /// <summary>The credential was refused (401/403).</summary>
    Authentication,
    /// <summary>Throttled (429).</summary>
    RateLimit,
    /// <summary>Transport failure, DNS, connection reset, or a 5xx.</summary>
    Availability,
    /// <summary>The attempt exceeded its deadline.</summary>
    Timeout,
    /// <summary>A 200 arrived but the body could not be interpreted.</summary>
    MalformedResponse
}

/// <summary>
/// Implemented by a provider's own "the payload was uninterpretable" exception, so the policy can
/// classify it without referencing an infrastructure type.
/// </summary>
public interface IProviderMalformedResponseException;

/// <summary>
/// Classifies provider failures and decides the consequences. The design review's point was that
/// treating every error alike lets one bad request (a marine query to an inland coordinate, which
/// legitimately answers "no coverage") open a breaker and degrade weather for every user, so the
/// classification is explicit rather than inferred from "did it throw".
/// </summary>
public static class ProviderFailurePolicy
{
    public static ProviderFailureKind Classify(Exception? exception) => exception switch
    {
        null => ProviderFailureKind.None,
        ProviderRateLimitedException => ProviderFailureKind.RateLimit,
        ProviderAuthenticationException => ProviderFailureKind.Authentication,
        ProviderRequestException => ProviderFailureKind.Rejection,
        ProviderUnavailableException => ProviderFailureKind.Availability,
        IProviderMalformedResponseException => ProviderFailureKind.MalformedResponse,
        TaskCanceledException or OperationCanceledException => ProviderFailureKind.Timeout,
        TimeoutException => ProviderFailureKind.Timeout,
        HttpRequestException => ProviderFailureKind.Availability,
        AllProvidersUnavailableException => ProviderFailureKind.Availability,
        _ => ProviderFailureKind.Availability
    };

    /// <summary>
    /// Only availability-class failures count against a provider's health. A 400 caused by our own bad
    /// request says nothing about the provider's ability to serve everyone else.
    /// </summary>
    public static bool AffectsProviderHealth(ProviderFailureKind kind) => kind switch
    {
        ProviderFailureKind.Availability => true,
        ProviderFailureKind.Timeout => true,
        ProviderFailureKind.RateLimit => true,
        ProviderFailureKind.MalformedResponse => true,
        _ => false
    };

    /// <summary>
    /// Substitution is allowed only for availability-class failures. A rejection means the request is
    /// wrong, so asking a second provider the same wrong question wastes quota and hides the real bug.
    /// </summary>
    public static bool AllowsFailover(ProviderFailureKind kind) => kind switch
    {
        ProviderFailureKind.Availability => true,
        ProviderFailureKind.Timeout => true,
        ProviderFailureKind.RateLimit => true,
        ProviderFailureKind.MalformedResponse => true,
        _ => false
    };

    /// <summary>
    /// The rule the API contract exposes: naming a provider is a preference, and only
    /// <c>strictProvider</c> turns it into a requirement. Strict callers receive the failure instead of
    /// a silent substitute, because they may be comparing providers.
    /// </summary>
    public static bool MaySubstitute(string? requestedProviderId, bool strictProvider, ProviderFailureKind kind)
    {
        if (string.IsNullOrWhiteSpace(requestedProviderId))
        {
            return true;
        }

        return !strictProvider && AllowsFailover(kind);
    }

    /// <summary>Stable outcome name recorded in the call record and diagnostics.</summary>
    public static string ToOutcomeName(ProviderFailureKind kind) => kind switch
    {
        ProviderFailureKind.RateLimit => "rate-limited",
        ProviderFailureKind.Authentication => "authentication-failed",
        ProviderFailureKind.Rejection => "request-rejected",
        ProviderFailureKind.Timeout => "timeout",
        ProviderFailureKind.MalformedResponse => "malformed-response",
        ProviderFailureKind.Availability => "provider-unavailable",
        _ => "none"
    };
}
