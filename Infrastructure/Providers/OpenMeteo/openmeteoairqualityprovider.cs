using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>Open-Meteo's air-quality dataset, exposed through the cross-provider <see cref="IAirQualityProvider"/> contract.</summary>
public sealed class OpenMeteoAirQualityProvider(OpenMeteoClient client, ProviderCallRecorder recorder) : IAirQualityProvider
{
    /// <summary>European and US AQI, the core pollutants, dust, UV and the full pollen set.</summary>
    public static IReadOnlyList<string> CurrentVariables { get; } =
    [
        "european_aqi", "us_aqi", "pm10", "pm2_5", "carbon_monoxide", "nitrogen_dioxide",
        "sulphur_dioxide", "ozone", "dust", "uv_index", "aerosol_optical_depth", "ammonia",
        "alder_pollen", "birch_pollen", "grass_pollen", "mugwort_pollen", "olive_pollen", "ragweed_pollen"
    ];

    public static IReadOnlyList<string> HourlyVariables { get; } = ["pm10", "pm2_5", "european_aqi", "us_aqi"];

    public string ProviderId => OpenMeteoProviderConstants.ProviderId;

    public async Task<AirQualityReading> GetCurrentAsync(Coordinates location, CancellationToken ct)
    {
        var envelope = await GetSeriesAsync(location, ct);
        var reading = OpenMeteoMapper.ToAirQualityReading(envelope, location);

        await recorder.RecordObservationAsync(new ObservationRecord
        {
            Id = ObservationRecord.BuildId(ProviderId, "air-quality", location.Latitude, location.Longitude, reading.MeasuredAt),
            ProviderId = ProviderId,
            Kind = "air-quality",
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            ObservedAt = reading.MeasuredAt,
            RecordedAt = DateTimeOffset.UtcNow,
            AqiValue = reading.Index.Value,
            AqiCategory = reading.Index.Category,
            DominantPollutant = reading.Index.DominantPollutant,
            PollutantCount = reading.Pollutants.Count
        }, ct);

        return reading;
    }

    /// <summary>Current plus hourly air quality for trend views.</summary>
    public async Task<WeatherSeries> GetAirQualitySeriesAsync(Coordinates location, CancellationToken ct)
    {
        var envelope = await GetSeriesAsync(location, ct);
        return OpenMeteoMapper.ToSeries(envelope);
    }

    private Task<OpenMeteoEnvelope> GetSeriesAsync(Coordinates location, CancellationToken ct)
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.AirQuality,
            Location = location,
            Groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["current"] = CurrentVariables,
                ["hourly"] = HourlyVariables
            }
        };

        return client.GetSeriesAsync(request, ct);
    }
}
