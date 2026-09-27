using Domain.Entities;
using Domain.Entities.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Providers;

/// <summary>
/// The single seam that turns an in-flight provider call into durable state: a call record, an
/// updated last-success/last-failure view, and — for readings we intend to keep — a modelled
/// observation. The Open-Meteo client calls it for every outcome, including failures, so operators
/// can always answer "what did we send and what came back".
/// </summary>
public sealed class ProviderCallRecorder(
    IProviderCallRecordStore records,
    IProviderDiagnosticsStore diagnostics,
    IObservationStore observations,
    ILogger<ProviderCallRecorder> logger)
{
    public Task RecordAsync(ProviderCallRecord record, CancellationToken ct) => records.AppendAsync(record, ct);

    public Task RecordSuccessAsync(string providerId, DateTimeOffset at, string operation, CancellationToken ct) =>
        diagnostics.RecordSuccessAsync(providerId, at, operation, ct);

    /// <summary>Idempotent: the observation id is derived from the reading, so replays update in place.</summary>
    public Task<ObservationRecord> RecordObservationAsync(ObservationRecord observation, CancellationToken ct) =>
        observations.UpsertAsync(observation, ct);

    public async Task RecordFailureAsync(
        string providerId,
        string operation,
        string outcome,
        string message,
        string sanitizedUrl,
        TimeSpan elapsed,
        int? httpStatus,
        CancellationToken ct)
    {
        var at = DateTimeOffset.UtcNow;

        await records.AppendAsync(new ProviderCallRecord
        {
            Id = Guid.NewGuid().ToString("n"),
            ProviderId = providerId,
            Operation = operation,
            SanitizedUrl = sanitizedUrl,
            StartedAt = at - elapsed,
            DurationMs = (long)elapsed.TotalMilliseconds,
            Outcome = outcome,
            HttpStatus = httpStatus,
            ErrorCode = outcome,
            ErrorMessage = message,
            ResponseBytes = 0
        }, ct);

        await diagnostics.RecordFailureAsync(providerId, at, operation, outcome, message, ct);

        logger.LogInformation("Recorded provider failure {Outcome} for {Provider}/{Operation}", outcome, providerId, operation);
    }
}
