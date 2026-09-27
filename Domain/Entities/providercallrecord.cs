namespace Domain.Entities;

/// <summary>
/// One durable record of a provider interaction: what we asked for, what came back, how long it
/// took and how it ended. Answers "what did we send, what came back" after the fact.
/// </summary>
public sealed record ProviderCallRecord
{
    public required string Id { get; init; }

    public required string ProviderId { get; init; }

    /// <summary>Provider operation, e.g. "forecast", "air-quality", "geocoding-search".</summary>
    public required string Operation { get; init; }

    /// <summary>The request URL with any credential masked.</summary>
    public required string SanitizedUrl { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required long DurationMs { get; init; }

    /// <summary>One of: success, request-rejected, authentication-failed, rate-limited, provider-unavailable, malformed-response, timeout.</summary>
    public required string Outcome { get; init; }

    public int? HttpStatus { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public int RetryAttempts { get; init; }

    public long ResponseBytes { get; init; }

    /// <summary>Points parsed from the payload, when the call returned a time series.</summary>
    public int? PointCount { get; init; }

    /// <summary>Optional opaque payload kept for audit; never contains credential material.</summary>
    public string? Payload { get; init; }

    /// <summary>Correlation id shared with the structured log entry for the same call.</summary>
    public string? CorrelationId { get; init; }

    public bool Succeeded => Outcome == "success";
}

/// <summary>Human-readable summary of the most recent successful and failed synchronisation for a provider.</summary>
public sealed record ProviderDiagnostics
{
    public required string ProviderId { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public string? LastSuccessOperation { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }

    public string? LastFailureOperation { get; init; }

    public string? LastFailureOutcome { get; init; }

    public string? LastFailureMessage { get; init; }

    public int ConsecutiveFailures { get; init; }

    public long TotalSuccesses { get; init; }

    public long TotalFailures { get; init; }
}
