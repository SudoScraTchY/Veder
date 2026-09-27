using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Open-Meteo as the product's primary weather provider. Implements the cross-provider
/// <see cref="IWeatherProvider"/> contract, so it participates in the existing registry/selector
/// flow (cache-first lookup, priority fallback, no silent substitution) without any special casing,
/// and additionally exposes the full series through <see cref="IWeatherSeriesProvider"/>.
/// </summary>
public sealed class OpenMeteoWeatherProvider(OpenMeteoClient client, ProviderCallRecorder recorder)
    : IWeatherProvider, IWeatherSeriesProvider
{
    /// <summary>Variables requested for every forecast read; kept here so the capability is explicit and testable.</summary>
    public static IReadOnlyList<string> CurrentVariables { get; } =
    [
        "temperature_2m", "relative_humidity_2m", "apparent_temperature", "precipitation",
        "weather_code", "cloud_cover", "pressure_msl", "wind_speed_10m", "wind_direction_10m", "wind_gusts_10m"
    ];

    public static IReadOnlyList<string> HourlyVariables { get; } = ["precipitation_probability", "temperature_2m"];

    public static IReadOnlyList<string> DailyVariables { get; } =
        ["temperature_2m_max", "temperature_2m_min", "weather_code", "sunrise", "sunset", "precipitation_sum"];

    public string ProviderId => OpenMeteoProviderConstants.ProviderId;

    public async Task<WeatherForecast> GetForecastAsync(Coordinates location, CancellationToken ct)
    {
        var envelope = await client.GetSeriesAsync(NewRequest(location), ct);
        var forecast = OpenMeteoMapper.ToWeatherForecast(envelope, location);

        await recorder.RecordObservationAsync(new ObservationRecord
        {
            Id = ObservationRecord.BuildId(ProviderId, "weather", location.Latitude, location.Longitude, forecast.ValidAt),
            ProviderId = ProviderId,
            Kind = "weather",
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            ObservedAt = forecast.ValidAt,
            RecordedAt = DateTimeOffset.UtcNow,
            Summary = forecast.Summary,
            TemperatureC = forecast.Temperature.Celsius,
            HumidityPercent = forecast.Humidity,
            WindSpeedKph = forecast.WindSpeed
        }, ct);

        return forecast;
    }

    /// <summary>Full series read (current + hourly + daily) for callers that need more than the current point.</summary>
    public async Task<WeatherSeries> GetForecastSeriesAsync(Coordinates location, CancellationToken ct)
    {
        var envelope = await client.GetSeriesAsync(NewRequest(location), ct);
        return OpenMeteoMapper.ToSeries(envelope);
    }

    private static OpenMeteoRequest NewRequest(Coordinates location) => new()
    {
        Dataset = OpenMeteoDataset.Forecast,
        Location = location,
        Groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["current"] = CurrentVariables,
            ["hourly"] = HourlyVariables,
            ["daily"] = DailyVariables
        }
    };
}
