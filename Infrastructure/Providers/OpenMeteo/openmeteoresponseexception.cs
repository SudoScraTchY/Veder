using Domain.Caching;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Raised when Open-Meteo returns something we cannot interpret (unexpected shape, unparseable
/// payload). Distinct from <c>ProviderRequestException</c> (our request was rejected) and
/// <c>ProviderUnavailableException</c> (transport/availability), so callers can react differently.
/// </summary>
public sealed class OpenMeteoResponseException : Exception, IProviderMalformedResponseException
{
    private const int MaxSnippetLength = 512;

    public OpenMeteoResponseException(string providerId, string operation, string detail, string payload)
        : base($"Open-Meteo {operation} response for '{providerId}' could not be interpreted: {detail}")
    {
        ProviderId = providerId;
        Operation = operation;
        Detail = detail;
        PayloadSnippet = payload.Length <= MaxSnippetLength ? payload : payload[..MaxSnippetLength] + "…";
    }

    public string ProviderId { get; }

    public string Operation { get; }

    public string Detail { get; }

    /// <summary>A truncated, non-secret copy of the offending payload for diagnostics.</summary>
    public string PayloadSnippet { get; }
}
