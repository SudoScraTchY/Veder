using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>Maps Open-Meteo payloads onto the product's provider-agnostic domain model.</summary>
public static class OpenMeteoMapper
{
    private static readonly Dictionary<int, string> WeatherCodes = new()
    {
        [0] = "Clear sky",
        [1] = "Mainly clear",
        [2] = "Partly cloudy",
        [3] = "Overcast",
        [45] = "Fog",
        [48] = "Depositing rime fog",
        [51] = "Light drizzle",
        [53] = "Moderate drizzle",
        [55] = "Dense drizzle",
        [56] = "Light freezing drizzle",
        [57] = "Dense freezing drizzle",
        [61] = "Slight rain",
        [63] = "Moderate rain",
        [65] = "Heavy rain",
        [66] = "Light freezing rain",
        [67] = "Heavy freezing rain",
        [71] = "Slight snowfall",
        [73] = "Moderate snowfall",
        [75] = "Heavy snowfall",
        [77] = "Snow grains",
        [80] = "Slight rain showers",
        [81] = "Moderate rain showers",
        [82] = "Violent rain showers",
        [85] = "Slight snow showers",
        [86] = "Heavy snow showers",
        [95] = "Thunderstorm",
        [96] = "Thunderstorm with slight hail",
        [99] = "Thunderstorm with heavy hail"
    };

    /// <summary>WMO 4677 weather code to a short human phrase; unknown codes are reported as such rather than guessed.</summary>
    public static string DescribeWeatherCode(double? code) => code is null
        ? "Unknown conditions"
        : WeatherCodes.TryGetValue((int)code.Value, out var text) ? text : $"WMO code {(int)code.Value}";

    /// <summary>
    /// Projects the current block of a forecast payload onto <see cref="WeatherForecast"/>, using the
    /// hourly series only to fill the precipitation probability for the current hour.
    /// </summary>
    public static WeatherForecast ToWeatherForecast(OpenMeteoEnvelope envelope, Coordinates requested)
    {
        var current = envelope.Current
            ?? throw new OpenMeteoResponseException(
                OpenMeteoProviderConstants.ProviderId, envelope.Series.Operation, "payload contained no current observation", string.Empty);

        double Get(string name, double fallback = 0) =>
            current.Values.TryGetValue(name, out var value) ? value : fallback;

        var precipitationProbability = envelope.Series.Points
            .Where(p => p.Time <= current.Time)
            .OrderByDescending(p => p.Time)
            .Select(p => p.Values.TryGetValue("precipitation_probability", out var v) ? v : (double?)null)
            .FirstOrDefault(v => v is not null) ?? 0;

        return new WeatherForecast(
            Location: requested,
            Temperature: Temperature.FromCelsius(Get("temperature_2m")),
            ValidAt: current.Time,
            Summary: DescribeWeatherCode(current.Values.TryGetValue("weather_code", out var code) ? code : null),
            Humidity: Get("relative_humidity_2m"),
            WindSpeed: Get("wind_speed_10m"),
            WindDirection: (int)Math.Round(Get("wind_direction_10m")),
            Pressure: Get("pressure_msl"),
            PrecipitationProbability: precipitationProbability,
            SourceCapabilities: ProviderCapability.CurrentWeather | ProviderCapability.Forecast,
            ProviderId: OpenMeteoProviderConstants.ProviderId);
    }

    /// <summary>Projects the current block of an air-quality payload, preferring the European AQI when present.</summary>
    public static AirQualityReading ToAirQualityReading(OpenMeteoEnvelope envelope, Coordinates requested)
    {
        var current = envelope.Current
            ?? throw new OpenMeteoResponseException(
                OpenMeteoProviderConstants.ProviderId, envelope.Series.Operation, "payload contained no current observation", string.Empty);

        var aqiValue = current.Values.TryGetValue("european_aqi", out var european)
            ? european
            : current.Values.TryGetValue("us_aqi", out var us) ? us : 0;

        var pollutants = current.Values
            .Where(kvp => kvp.Key is not ("european_aqi" or "us_aqi"))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new AirQualityReading(
            Location: requested,
            Index: AirQualityIndex.FromValue((int)Math.Round(aqiValue)),
            MeasuredAt: current.Time,
            Pollutants: pollutants,
            SourceCapabilities: ProviderCapability.AirQuality,
            ProviderId: OpenMeteoProviderConstants.ProviderId);
    }

    /// <summary>Flattens a payload into the provider-agnostic series the domain exposes.</summary>
    public static WeatherSeries ToSeries(OpenMeteoEnvelope envelope) => envelope.Series;
}
