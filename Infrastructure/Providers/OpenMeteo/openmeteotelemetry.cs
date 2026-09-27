using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Open-Meteo's metrics and tracing surface. Kept in one place so every call site reports the same
/// instrument names, and so an operator can build dashboards from a single documented list.
/// </summary>
public sealed class OpenMeteoTelemetry : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _bytes;
    private readonly Histogram<double> _duration;

    public OpenMeteoTelemetry()
    {
        _meter = new Meter(OpenMeteoProviderConstants.MetricPrefix, "1.0.0");
        _requests = _meter.CreateCounter<long>($"{OpenMeteoProviderConstants.MetricPrefix}.request.count", "calls", "Open-Meteo calls attempted.");
        _failures = _meter.CreateCounter<long>($"{OpenMeteoProviderConstants.MetricPrefix}.request.failures", "calls", "Open-Meteo calls that ended in an error.");
        _rateLimited = _meter.CreateCounter<long>($"{OpenMeteoProviderConstants.MetricPrefix}.request.rate_limited", "calls", "Open-Meteo calls rejected with HTTP 429.");
        _retries = _meter.CreateCounter<long>($"{OpenMeteoProviderConstants.MetricPrefix}.request.retries", "retries", "Transport level retries observed by the provider.");
        _bytes = _meter.CreateCounter<long>($"{OpenMeteoProviderConstants.MetricPrefix}.response.bytes", "By", "Bytes received from Open-Meteo.");
        _duration = _meter.CreateHistogram<double>($"{OpenMeteoProviderConstants.MetricPrefix}.request.duration", "ms", "Wall clock duration of an Open-Meteo call.");
    }

    public static ActivitySource ActivitySource { get; } = new(OpenMeteoProviderConstants.ActivitySourceName, "1.0.0");

    public void RecordRequest(string operation) =>
        _requests.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void RecordFailure(string operation, string errorType) =>
        _failures.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("error.type", errorType));

    public void RecordRateLimited(string operation) =>
        _rateLimited.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void RecordRetries(string operation, int attempts) =>
        _retries.Add(attempts, new KeyValuePair<string, object?>("operation", operation));

    public void RecordBytes(string operation, long bytes) =>
        _bytes.Add(bytes, new KeyValuePair<string, object?>("operation", operation));

    public void RecordDuration(string operation, double milliseconds) =>
        _duration.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));

    public void Dispose() => _meter.Dispose();
}
