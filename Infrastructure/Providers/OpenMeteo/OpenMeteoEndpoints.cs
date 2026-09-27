namespace Infrastructure.Providers.OpenMeteo;

/// <summary>Public Open-Meteo hosts, one per documented dataset. All are reachable without an API key on the free tier.</summary>
public static class OpenMeteoEndpoints
{
    public const string Forecast = "https://api.open-meteo.com/v1/forecast";
    public const string Archive = "https://archive-api.open-meteo.com/v1/archive";
    public const string HistoricalForecast = "https://historical-forecast-api.open-meteo.com/v1/forecast";
    public const string AirQuality = "https://air-quality-api.open-meteo.com/v1/air-quality";
    public const string Marine = "https://marine-api.open-meteo.com/v1/marine";
    public const string Ensemble = "https://ensemble-api.open-meteo.com/v1/ensemble";
    public const string Climate = "https://climate-api.open-meteo.com/v1/climate";
    public const string Flood = "https://flood-api.open-meteo.com/v1/flood";
    public const string Elevation = "https://api.open-meteo.com/v1/elevation";

    /// <summary>Place-name search. Returns {"results": [...]}.</summary>
    public const string GeocodingSearch = "https://geocoding-api.open-meteo.com/v1/search";

    /// <summary>Single place by id (<c>?id=</c>). Returns one bare object, not a results envelope.</summary>
    public const string GeocodingGet = "https://geocoding-api.open-meteo.com/v1/get";
}
