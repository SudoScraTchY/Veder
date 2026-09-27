using System.Globalization;

namespace Domain.Caching;

/// <summary>The kinds of payload the cache stores. TTL and key shape are decided per type.</summary>
public enum WeatherDataType
{
    CurrentConditions,
    AirQuality,
    Forecast,
    Marine,
    Archive,
    Elevation,
    Geocoding
}

/// <summary>
/// Everything that changes the bytes of a cached payload. Any field added here must be part of the
/// key: the review of the first design caught that units, requested variables, model selection and
/// timezone all change the response while leaving the key identical, which serves 22 °C as 22 °F.
/// </summary>
public sealed record WeatherCacheKeyParts
{
    public required WeatherDataType DataType { get; init; }

    public required string ProviderId { get; init; }

    public required GeoBucket Bucket { get; init; }

    public string Units { get; init; } = "metric";

    public IReadOnlyList<string> Variables { get; init; } = [];

    public string? Models { get; init; }

    public string Timezone { get; init; } = "auto";

    /// <summary>Bump when the cached shape changes, so old entries can never be deserialised as the new one.</summary>
    public int SchemaVersion { get; init; } = WeatherCacheKey.CurrentSchemaVersion;
}

/// <summary>Composes stable, culture-invariant cache keys.</summary>
public static class WeatherCacheKey
{
    public const int CurrentSchemaVersion = 1;

    private const string Separator = "|";
    private const string Empty = "-";

    public static string Compose(WeatherCacheKeyParts parts)
    {
        var variables = string.Join(
            ",",
            parts.Variables
                .Select(v => v.Trim().ToLowerInvariant())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal));

        return string.Join(
            Separator,
            $"v{parts.SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            parts.DataType.ToString(),
            parts.ProviderId.Trim().ToLowerInvariant(),
            parts.Bucket.ToString(),
            parts.Units.Trim().ToLowerInvariant(),
            variables.Length == 0 ? Empty : variables,
            string.IsNullOrWhiteSpace(parts.Models) ? Empty : parts.Models!.Trim().ToLowerInvariant(),
            parts.Timezone.Trim().ToLowerInvariant());
    }

    /// <summary>Prefix matching every entry for one data type and provider, used by admin invalidation.</summary>
    public static string PrefixFor(WeatherDataType dataType, string providerId) =>
        string.Join(Separator, $"v{CurrentSchemaVersion}", dataType.ToString(), providerId.Trim().ToLowerInvariant());
}

/// <summary>
/// Per-type lifetimes. Weather does not change instantaneously, so a short window removes most
/// upstream traffic without serving anything the user could notice as wrong.
/// </summary>
public static class CacheTtlPolicy
{
    public static TimeSpan For(WeatherDataType dataType) => dataType switch
    {
        WeatherDataType.CurrentConditions => TimeSpan.FromMinutes(7),
        WeatherDataType.AirQuality => TimeSpan.FromMinutes(45),
        WeatherDataType.Forecast => TimeSpan.FromHours(4),
        WeatherDataType.Marine => TimeSpan.FromHours(4),
        WeatherDataType.Archive => TimeSpan.FromHours(24),
        WeatherDataType.Elevation => TimeSpan.FromDays(30),
        WeatherDataType.Geocoding => TimeSpan.FromDays(30),
        _ => TimeSpan.FromMinutes(10)
    };

    /// <summary>
    /// Spreads expiry so a hot entry cannot be refreshed by every caller at the same instant.
    /// </summary>
    public static TimeSpan WithJitter(TimeSpan ttl, double fraction = 0.1, Func<double>? nextRandom = null)
    {
        var clamped = Math.Clamp(fraction, 0, 0.5);
        var sample = (nextRandom ?? Random.Shared.NextDouble)();
        var offset = (sample * 2 - 1) * clamped;

        return ttl * (1 + offset);
    }
}
