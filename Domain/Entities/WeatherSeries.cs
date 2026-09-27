using Domain.Entities.ValueObjects;

namespace Domain.Entities;

/// <summary>One timestamped sample of a weather-style time series; values are keyed by the provider's variable name.</summary>
public sealed record SeriesPoint(DateTimeOffset Time, IReadOnlyDictionary<string, double> Values);

/// <summary>A provider-agnostic time series (forecast, archive, marine, ensemble, climate, flood).</summary>
public sealed record WeatherSeries(
    Coordinates Location,
    string ProviderId,
    DateTimeOffset FetchedAt,
    string Operation,
    string? Timezone,
    int UtcOffsetSeconds,
    double? ElevationMeters,
    IReadOnlyDictionary<string, string> Units,
    IReadOnlyList<SeriesPoint> Points);
