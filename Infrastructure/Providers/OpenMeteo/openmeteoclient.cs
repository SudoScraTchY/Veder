using System.Net;
using System.Text.Json;
using Domain.Entities;
using Domain.Entities.Exceptions;
using Domain.Entities.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Transport for every Open-Meteo dataset. One place owns request rendering, status-to-exception
/// mapping, telemetry and durable recording, so each dataset adapter above it stays declarative.
/// </summary>
public sealed class OpenMeteoClient(
    HttpClient http,
    IOptions<OpenMeteoOptions> options,
    OpenMeteoTelemetry telemetry,
    ILogger<OpenMeteoClient> logger,
    ProviderCallRecorder recorder,
    ProviderConcurrencyGate gate)
{
    private static readonly JsonSerializerOptions RawOptions = new(JsonSerializerDefaults.Web);

    public OpenMeteoOptions Options => options.Value;

    /// <summary>Executes a validated series request and returns the parsed envelope.</summary>
    public async Task<OpenMeteoEnvelope> GetSeriesAsync(OpenMeteoRequest request, CancellationToken ct)
    {
        var built = OpenMeteoRequestBuilder.Build(request, options.Value, DateOnly.FromDateTime(DateTime.UtcNow));
        var fetchedAt = DateTimeOffset.UtcNow;
        var body = await SendAsync(built, ct);

        var envelope = OpenMeteoResponseParser.Parse(
            json: body,
            providerId: OpenMeteoProviderConstants.ProviderId,
            operation: built.Operation,
            requestedLocation: request.Location,
            groups: built.Groups,
            fetchedAt: fetchedAt);

        await recorder.RecordAsync(new ProviderCallRecord
        {
            Id = Guid.NewGuid().ToString("n"),
            ProviderId = OpenMeteoProviderConstants.ProviderId,
            Operation = built.Operation,
            SanitizedUrl = built.SanitizedUrl,
            StartedAt = fetchedAt,
            DurationMs = (long)(DateTimeOffset.UtcNow - fetchedAt).TotalMilliseconds,
            Outcome = "success",
            HttpStatus = (int)HttpStatusCode.OK,
            ResponseBytes = body.Length,
            PointCount = envelope.Series.Points.Count,
            Payload = Options.RecordCalls ? body : null
        }, ct);

        await recorder.RecordSuccessAsync(OpenMeteoProviderConstants.ProviderId, fetchedAt, built.Operation, ct);

        return envelope;
    }

    /// <summary>Elevation lookup for up to 100 coordinates in a single call.</summary>
    public async Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<Coordinates> locations, CancellationToken ct)
    {
        if (locations.Count == 0)
        {
            return [];
        }

        if (locations.Count > 100)
        {
            throw new ProviderRequestException(OpenMeteoProviderConstants.ProviderId, "the elevation endpoint accepts at most 100 coordinates per call");
        }

        var latitude = string.Join(",", locations.Select(l => l.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var longitude = string.Join(",", locations.Select(l => l.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var url = $"{OpenMeteoEndpoints.Elevation}?latitude={latitude}&longitude={longitude}";
        var built = new OpenMeteoBuiltRequest(OpenMeteoDataset.Forecast, "elevation", url, url, [], []);

        var body = await SendAsync(built, ct);
        await recorder.RecordSuccessAsync(OpenMeteoProviderConstants.ProviderId, DateTimeOffset.UtcNow, "elevation", ct);

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("elevation", out var elevations) || elevations.ValueKind != JsonValueKind.Array)
        {
            throw new OpenMeteoResponseException(OpenMeteoProviderConstants.ProviderId, "elevation", "response carried no 'elevation' array", body);
        }

        return elevations.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var value) ? value : double.NaN)
            .ToList();
    }

    /// <summary>Place-name search. Count is clamped to the documented 1..100 range.</summary>
    public async Task<IReadOnlyList<GeoLocation>> SearchLocationsAsync(string name, int count, string? language, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProviderRequestException(OpenMeteoProviderConstants.ProviderId, "geocoding search requires a non-empty name");
        }

        var clamped = Math.Clamp(count, 1, 100);
        var query = new List<string>
        {
            $"name={Uri.EscapeDataString(name)}",
            $"count={clamped}",
            "format=json"
        };

        if (!string.IsNullOrWhiteSpace(language))
        {
            query.Add($"language={Uri.EscapeDataString(language!)}");
        }

        var url = $"{OpenMeteoEndpoints.GeocodingSearch}?{string.Join("&", query)}";
        var built = new OpenMeteoBuiltRequest(OpenMeteoDataset.Forecast, "geocoding-search", url, url, [], []);

        var body = await SendAsync(built, ct);
        var results = OpenMeteoGeocodingParser.Parse(body, "geocoding-search");
        await recorder.RecordSuccessAsync(OpenMeteoProviderConstants.ProviderId, DateTimeOffset.UtcNow, "geocoding-search", ct);

        await recorder.RecordAsync(new ProviderCallRecord
        {
            Id = Guid.NewGuid().ToString("n"),
            ProviderId = OpenMeteoProviderConstants.ProviderId,
            Operation = "geocoding-search",
            SanitizedUrl = url,
            StartedAt = DateTimeOffset.UtcNow,
            DurationMs = 0,
            Outcome = "success",
            HttpStatus = 200,
            ResponseBytes = body.Length,
            PointCount = results.Count,
            Payload = Options.RecordCalls ? body : null
        }, ct);

        return results;
    }

    /// <summary>
    /// Resolves one or more geocoding ids. The documented lookup is <c>/v1/get?id=</c> and returns a
    /// single bare place object, so ids are resolved one call at a time.
    /// </summary>
    public async Task<IReadOnlyList<GeoLocation>> GetLocationsByIdAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var found = new List<GeoLocation>();

        foreach (var id in ids)
        {
            var url = $"{OpenMeteoEndpoints.GeocodingGet}?id={id}&format=json";
            var built = new OpenMeteoBuiltRequest(OpenMeteoDataset.Forecast, "geocoding-get", url, url, [], []);

            var body = await SendAsync(built, ct);
            found.AddRange(OpenMeteoGeocodingParser.Parse(body, "geocoding-get"));
        }

        await recorder.RecordSuccessAsync(OpenMeteoProviderConstants.ProviderId, DateTimeOffset.UtcNow, "geocoding-get", ct);
        return found;
    }

    /// <summary>Sends the request, maps every documented failure mode onto a typed exception and emits telemetry.</summary>
    private async Task<string> SendAsync(OpenMeteoBuiltRequest built, CancellationToken ct)
    {
        using var activity = OpenMeteoTelemetry.ActivitySource.StartActivity($"openmeteo.{built.Operation}");
        activity?.SetTag("openmeteo.operation", built.Operation);
        activity?.SetTag("openmeteo.url", built.SanitizedUrl);

        telemetry.RecordRequest(built.Operation);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        HttpResponseMessage response;
        using var lease = await gate.AcquireAsync(ct);

        try
        {
            response = await http.GetAsync(built.Url, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            telemetry.RecordDuration(built.Operation, stopwatch.Elapsed.TotalMilliseconds);
            telemetry.RecordFailure(built.Operation, "timeout");

            logger.LogWarning(ex, "Open-Meteo {Operation} timed out after {ElapsedMs} ms ({Url})",
                built.Operation, (long)stopwatch.Elapsed.TotalMilliseconds, built.SanitizedUrl);

            await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation, "timeout",
                $"the request exceeded the {Options.TimeoutSeconds}s timeout", built.SanitizedUrl, stopwatch.Elapsed, null, ct);

            throw new ProviderUnavailableException(OpenMeteoProviderConstants.ProviderId);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            telemetry.RecordDuration(built.Operation, stopwatch.Elapsed.TotalMilliseconds);
            telemetry.RecordFailure(built.Operation, "transport");

            logger.LogWarning(ex, "Open-Meteo {Operation} failed at the transport layer ({Url})", built.Operation, built.SanitizedUrl);

            await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation, "provider-unavailable",
                ex.Message, built.SanitizedUrl, stopwatch.Elapsed, null, ct);

            throw new ProviderUnavailableException(OpenMeteoProviderConstants.ProviderId);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();

            var elapsed = (long)stopwatch.Elapsed.TotalMilliseconds;
            telemetry.RecordDuration(built.Operation, stopwatch.Elapsed.TotalMilliseconds);
            telemetry.RecordBytes(built.Operation, body.Length);
            activity?.SetTag("openmeteo.status", (int)response.StatusCode);
            activity?.SetTag("openmeteo.points.bytes", body.Length);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var reason = TryReadReason(body) ?? response.ReasonPhrase ?? "no reason supplied";
            var status = (int)response.StatusCode;

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    telemetry.RecordFailure(built.Operation, "authentication");
                    logger.LogError("Open-Meteo {Operation} rejected the credential (HTTP {Status}): {Reason}",
                        built.Operation, status, reason);
                    await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation,
                        "authentication-failed", reason, built.SanitizedUrl, stopwatch.Elapsed, status, ct);
                    throw new ProviderAuthenticationException(OpenMeteoProviderConstants.ProviderId, reason);

                case HttpStatusCode.TooManyRequests:
                    telemetry.RecordRateLimited(built.Operation);
                    telemetry.RecordFailure(built.Operation, "rate-limited");
                    var retryAfter = ReadRetryAfter(response);
                    logger.LogWarning("Open-Meteo {Operation} rate limited us (retry after {RetryAfter})", built.Operation, retryAfter);
                    await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation,
                        "rate-limited", reason, built.SanitizedUrl, stopwatch.Elapsed, status, ct);
                    throw new ProviderRateLimitedException(OpenMeteoProviderConstants.ProviderId, retryAfter);

                case HttpStatusCode.BadRequest:
                    telemetry.RecordFailure(built.Operation, "request-rejected");
                    logger.LogWarning("Open-Meteo {Operation} rejected the request: {Reason} ({Url})",
                        built.Operation, reason, built.SanitizedUrl);
                    await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation,
                        "request-rejected", reason, built.SanitizedUrl, stopwatch.Elapsed, status, ct);
                    throw new ProviderRequestException(OpenMeteoProviderConstants.ProviderId, reason);

                default:
                    telemetry.RecordFailure(built.Operation, "provider-unavailable");
                    logger.LogWarning("Open-Meteo {Operation} returned HTTP {Status}: {Reason}", built.Operation, status, reason);
                    await recorder.RecordFailureAsync(OpenMeteoProviderConstants.ProviderId, built.Operation,
                        "provider-unavailable", $"HTTP {status}: {reason}", built.SanitizedUrl, stopwatch.Elapsed, status, ct);
                    throw new ProviderUnavailableException(OpenMeteoProviderConstants.ProviderId);
            }
        }
    }

    private static string? TryReadReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("reason", out var reason) &&
                reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString();
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body is still useful for the message; fall through to a trimmed copy.
        }

        return body.Length <= 256 ? body : body[..256] + "…";
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
