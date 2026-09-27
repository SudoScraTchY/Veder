using System.Text.Json;
using Domain.Entities;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>Parses the geocoding endpoints into <see cref="GeoLocation"/> values.</summary>
public static class OpenMeteoGeocodingParser
{
    public static IReadOnlyList<GeoLocation> Parse(string json, string operation)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OpenMeteoResponseException(OpenMeteoProviderConstants.ProviderId, operation, $"payload is not valid JSON: {ex.Message}", json);
        }

        using (document)
        {
            var root = document.RootElement;

            // /v1/get returns one bare place object; /v1/search returns {"results": [...]}. Accept both.
            if (root.ValueKind == JsonValueKind.Object &&
                !root.TryGetProperty("results", out _) &&
                root.TryGetProperty("id", out _))
            {
                return [ReadLocation(root)];
            }

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                // A search with no matches legitimately returns no "results" member.
                return [];
            }

            var locations = new List<GeoLocation>();

            foreach (var element in results.EnumerateArray())
            {
                if (!element.TryGetProperty("latitude", out _) || !element.TryGetProperty("longitude", out _))
                {
                    continue;
                }

                locations.Add(ReadLocation(element));
            }

            return locations;
        }
    }

    private static GeoLocation ReadLocation(JsonElement element) => new(
        Id: element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : 0,
        Name: ReadString(element, "name") ?? string.Empty,
        Location: Coordinates.FromDegrees(
            element.TryGetProperty("latitude", out var lat) ? lat.GetDouble() : 0,
            element.TryGetProperty("longitude", out var lon) ? lon.GetDouble() : 0),
        ElevationMeters: ReadDouble(element, "elevation"),
        CountryCode: ReadString(element, "country_code"),
        Country: ReadString(element, "country"),
        Admin1: ReadString(element, "admin1"),
        Timezone: ReadString(element, "timezone"),
        Population: element.TryGetProperty("population", out var population) && population.ValueKind == JsonValueKind.Number
            ? population.GetInt64()
            : null,
        FeatureCode: ReadString(element, "feature_code"));

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
}
