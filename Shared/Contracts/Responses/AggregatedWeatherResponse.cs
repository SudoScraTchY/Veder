using Domain.Caching;
using Domain.Entities;
using Domain.Entities.ValueObjects;

namespace Shared.Contracts.Responses;

/// <summary>Where a payload came from, so a client can be honest about degradation and staleness.</summary>
public sealed record ResponseProvenance(
    /// <summary>What the caller asked for, or null when they did not name a provider.</summary>
    string? RequestedProvider,
    /// <summary>The provider that actually produced the data.</summary>
    string SourceProvider,
    /// <summary>True when a different provider answered than the one requested or preferred.</summary>
    bool Failover,
    /// <summary>True when the payload is past its freshness window (served stale because the provider failed).</summary>
    bool Degraded,
    /// <summary>How the read was satisfied: hit, miss, coalesced, stale-on-provider-failure, bypassed.</summary>
    string CacheOutcome,
    /// <summary>Age of the payload in seconds at the moment it was served.</summary>
    double AgeSeconds,
    /// <summary>When the payload was produced upstream.</summary>
    DateTimeOffset FetchedAt);

/// <summary>Location resolved for the request: the user's words, the coordinates used, and the cache cell.</summary>
public sealed record ResolvedLocation(
    string? RequestedName,
    double Latitude,
    double Longitude,
    string? CountryCode,
    string? Country,
    string? Admin1,
    string? Timezone,
    double CallerDistanceMeters);

/// <summary>One day of the forecast, already reduced to what a UI renders.</summary>
public sealed record DailyForecastItem(
    DateOnly Date,
    double TemperatureMaxC,
    double TemperatureMinC,
    string Summary,
    double PrecipitationMm,
    double WindSpeedMaxKph);

/// <summary>
/// The single consolidated payload the web app reads. One request returns location resolution,
/// current conditions, the daily outlook and air quality, each with its own provenance, so a partial
/// failure degrades one section rather than the whole page.
/// </summary>
public sealed record AggregatedWeatherResponse(
    ResolvedLocation Location,
    ResponseProvenance Provenance,
    CurrentConditionsBlock? Current,
    IReadOnlyList<DailyForecastItem> Forecast,
    AirQualityBlock? AirQuality,
    /// <summary>Sections that could not be served, with the reason - never silently omitted.</summary>
    IReadOnlyList<string> Warnings);

public sealed record CurrentConditionsBlock(
    double TemperatureC,
    string Summary,
    int HumidityPercent,
    double WindSpeedKph,
    int WindDirectionDegrees,
    double PressureHpa,
    DateTimeOffset ObservedAt);

public sealed record AirQualityBlock(
    int AqiValue,
    string AqiCategory,
    string DominantPollutant,
    int PollutantCount,
    DateTimeOffset MeasuredAt);

/// <summary>Assembles the aggregated payload from the pieces the use-case layer already produces.</summary>
public static class AggregatedWeatherComposer
{
    /// <summary>Metres between two coordinates, for reporting how far the served cell is from the request.</summary>
    public static double DistanceMeters(Coordinates a, Coordinates b)
    {
        const double earthRadius = 6371000d;
        var latitudeDelta = ToRadians(b.Latitude - a.Latitude);
        var longitudeDelta = ToRadians(b.Longitude - a.Longitude);
        var meanLatitude = ToRadians((a.Latitude + b.Latitude) / 2);

        var x = longitudeDelta * Math.Cos(meanLatitude);
        return Math.Sqrt(x * x + latitudeDelta * latitudeDelta) * earthRadius;
    }

    public static AggregatedWeatherResponse Compose(
        ResolvedLocation location,
        ResponseProvenance provenance,
        WeatherForecast? forecast,
        IReadOnlyList<DailyForecastItem>? daily,
        AirQualityReading? airQuality,
        IReadOnlyList<string>? warnings = null,
        bool includeAirQuality = true)
    {
        var issues = new List<string>(warnings ?? []);

        CurrentConditionsBlock? current = null;

        if (forecast is null)
        {
            issues.Add("Current conditions are unavailable.");
        }
        else
        {
            current = new CurrentConditionsBlock(
                TemperatureC: Math.Round(forecast.Temperature.Celsius, 1),
                Summary: forecast.Summary,
                HumidityPercent: (int)Math.Round(forecast.Humidity),
                WindSpeedKph: Math.Round(forecast.WindSpeed, 1),
                WindDirectionDegrees: forecast.WindDirection,
                PressureHpa: Math.Round(forecast.Pressure, 1),
                ObservedAt: forecast.ValidAt);
        }

        AirQualityBlock? air = null;

        if (includeAirQuality)
        {
            if (airQuality is null)
            {
                issues.Add("Air quality is unavailable.");
            }
            else
            {
                air = new AirQualityBlock(
                    AqiValue: airQuality.Index.Value,
                    AqiCategory: airQuality.Index.Category,
                    DominantPollutant: airQuality.Index.DominantPollutant,
                    PollutantCount: airQuality.Pollutants.Count,
                    MeasuredAt: airQuality.MeasuredAt);
            }
        }

        if (provenance.Degraded)
        {
            issues.Add($"Serving a stale reading from {provenance.SourceProvider}; the provider did not respond.");
        }

        if (provenance.Failover)
        {
            issues.Add($"Requested {provenance.RequestedProvider ?? "the preferred provider"}; served by {provenance.SourceProvider}.");
        }

        return new AggregatedWeatherResponse(
            Location: location,
            Provenance: provenance,
            Current: current,
            Forecast: daily ?? [],
            AirQuality: air,
            Warnings: issues);
    }

    /// <summary>Builds provenance from a cache read plus the provider that was asked for.</summary>
    public static ResponseProvenance ProvenanceFrom(
        string? requestedProvider,
        CacheReadResult<WeatherForecast> read,
        DateTimeOffset fetchedAt) => new(
            RequestedProvider: requestedProvider,
            SourceProvider: read.SourceProviderId,
            Failover: !string.IsNullOrWhiteSpace(requestedProvider) &&
                      !string.Equals(requestedProvider, read.SourceProviderId, StringComparison.OrdinalIgnoreCase),
            Degraded: read.IsDegraded,
            CacheOutcome: read.Outcome.ToString(),
            AgeSeconds: Math.Round(read.Age.TotalSeconds, 1),
            FetchedAt: fetchedAt);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
