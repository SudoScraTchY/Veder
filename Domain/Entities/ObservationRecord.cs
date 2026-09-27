namespace Domain.Entities;

/// <summary>
/// A durable observation synced from a provider — the modelled shape of what we read, as opposed to
/// the raw payload kept on the call record. <see cref="Id"/> is deterministic so replaying the same
/// reading updates rather than duplicates it.
/// </summary>
public sealed record ObservationRecord
{
    /// <summary>Deterministic key: provider | kind | location bucket | observation instant.</summary>
    public required string Id { get; init; }

    public required string ProviderId { get; init; }

    /// <summary>"weather" or "air-quality".</summary>
    public required string Kind { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>The instant the reading describes (provider time, normalised to UTC).</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public string? Summary { get; init; }

    public double? TemperatureC { get; init; }

    public double? HumidityPercent { get; init; }

    public double? WindSpeedKph { get; init; }

    public int? AqiValue { get; init; }

    public string? AqiCategory { get; init; }

    public string? DominantPollutant { get; init; }

    public int? PollutantCount { get; init; }

    public static string BuildId(string providerId, string kind, double latitude, double longitude, DateTimeOffset observedAt) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{providerId}|{kind}|{Math.Round(latitude, 2):0.##}:{Math.Round(longitude, 2):0.##}|{observedAt.ToUnixTimeSeconds()}");
}
