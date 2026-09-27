using System.Globalization;
using System.Text.Json;
using Domain.Entities;
using Domain.Entities.Exceptions;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>A parsed Open-Meteo response: the domain-shaped series plus the raw payload for the record store.</summary>
public sealed record OpenMeteoEnvelope(
    WeatherSeries Series,
    string RawJson,
    double? GenerationTimeMs,
    Coordinates GridPoint,
    /// <summary>The single "current" observation, when the request asked for the current grouping.</summary>
    SeriesPoint? Current);

/// <summary>
/// Turns an Open-Meteo JSON payload into <see cref="WeatherSeries"/>. Every dataset shares the same
/// envelope shape (metadata + per-group parallel arrays plus matching <c>&lt;group&gt;_units</c>
/// objects), so one parser serves forecast, archive, air quality, marine, ensemble, climate and flood.
/// </summary>
public static class OpenMeteoResponseParser
{
    public static OpenMeteoEnvelope Parse(
        string json,
        string providerId,
        string operation,
        Coordinates requestedLocation,
        IReadOnlyList<string> groups,
        DateTimeOffset fetchedAt)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OpenMeteoResponseException(providerId, operation, $"payload is not valid JSON: {ex.Message}", json);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OpenMeteoResponseException(providerId, operation, "payload is not a JSON object", json);
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
            {
                var reason = root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                    ? reasonElement.GetString() ?? "unspecified"
                    : "unspecified";

                throw new ProviderRequestException(providerId, reason);
            }

            var utcOffset = ReadInt(root, "utc_offset_seconds") ?? 0;
            var timezone = ReadString(root, "timezone");
            var elevation = ReadDouble(root, "elevation");
            var generationTime = ReadDouble(root, "generationtime_ms");

            var gridPoint = root.TryGetProperty("latitude", out _) && root.TryGetProperty("longitude", out _)
                ? Coordinates.FromDegrees(ReadDouble(root, "latitude") ?? requestedLocation.Latitude,
                                          ReadDouble(root, "longitude") ?? requestedLocation.Longitude)
                : requestedLocation;

            var units = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                MergeUnits(root, $"{group}_units", units);
            }

            MergeUnits(root, "current_units", units);
            MergeUnits(root, "hourly_units", units);
            MergeUnits(root, "daily_units", units);
            MergeUnits(root, "minutely_15_units", units);

            // The current block is a single observation, so it must not be merged into the time
            // series: every series point is ordered by time, and an hourly point would otherwise
            // sort before "now" and be mistaken for the current conditions.
            var current = ReadCurrentPoint(root, utcOffset);
            var points = ReadPoints(root, groups.Where(g => !string.Equals(g, "current", StringComparison.Ordinal)).ToList(), utcOffset);

            var series = new WeatherSeries(
                Location: requestedLocation,
                ProviderId: providerId,
                FetchedAt: fetchedAt,
                Operation: operation,
                Timezone: timezone,
                UtcOffsetSeconds: utcOffset,
                ElevationMeters: elevation,
                Units: units,
                Points: points);

            return new OpenMeteoEnvelope(series, json, generationTime, gridPoint, current);
        }
    }

    private static SeriesPoint? ReadCurrentPoint(JsonElement root, int utcOffsetSeconds)
    {
        if (!root.TryGetProperty("current", out var block) || block.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!block.TryGetProperty("time", out var time))
        {
            return null;
        }

        return new SeriesPoint(ReadTime(time, utcOffsetSeconds), ReadScalarValues(block));
    }

    private static List<SeriesPoint> ReadPoints(JsonElement root, IReadOnlyList<string> groups, int utcOffsetSeconds)
    {
        var points = new List<SeriesPoint>();

        foreach (var group in groups)
        {
            if (!root.TryGetProperty(group, out var block) || block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!block.TryGetProperty("time", out var time))
            {
                continue;
            }

            // A "current" block carries one scalar timestamp; every other group carries a parallel array.
            if (time.ValueKind != JsonValueKind.Array)
            {
                var values = ReadScalarValues(block);
                points.Add(new SeriesPoint(ReadTime(time, utcOffsetSeconds), values));
                continue;
            }

            var times = time.EnumerateArray().ToArray();
            for (var index = 0; index < times.Length; index++)
            {
                var values = new Dictionary<string, double>(StringComparer.Ordinal);

                foreach (var property in block.EnumerateObject())
                {
                    if (property.Name == "time" || property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var array = property.Value;
                    if (index >= array.GetArrayLength())
                    {
                        continue;
                    }

                    var element = array[index];
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
                    {
                        values[property.Name] = value;
                    }
                }

                points.Add(new SeriesPoint(ReadTime(times[index], utcOffsetSeconds), values));
            }
        }

        return points.OrderBy(p => p.Time).ToList();
    }

    private static Dictionary<string, double> ReadScalarValues(JsonElement block)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var property in block.EnumerateObject())
        {
            if (property.Name == "time")
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value))
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    private static void MergeUnits(JsonElement root, string propertyName, IDictionary<string, string> units)
    {
        if (!root.TryGetProperty(propertyName, out var block) || block.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in block.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                units[property.Name] = property.Value.GetString()!;
            }
        }
    }

    private static DateTimeOffset ReadTime(JsonElement element, int utcOffsetSeconds)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        if (text is null)
        {
            throw new OpenMeteoResponseException("open-meteo", "parse", "a time value was neither a number nor a string", string.Empty);
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            // ISO responses are expressed in the requested timezone; re-anchor them with the reported offset.
            return new DateTimeOffset(iso, TimeSpan.FromSeconds(utcOffsetSeconds));
        }

        throw new OpenMeteoResponseException("open-meteo", "parse", $"unrecognised time value '{text}'", string.Empty);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : null;

    private static double? ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
            ? value
            : null;
}
