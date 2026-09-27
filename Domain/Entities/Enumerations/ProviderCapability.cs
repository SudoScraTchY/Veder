namespace Domain.Entities.Enumerations;

[Flags]
public enum ProviderCapability
{
    None = 0,
    CurrentWeather = 1,
    Forecast = 2,
    AirQuality = 4,
    Historical = 8,
    Alerts = 16,
    Geocoding = 32,
    Elevation = 64,
    Marine = 128,
    Ensemble = 256,
    Climate = 512,
    Flood = 1024,
    All = CurrentWeather | Forecast | AirQuality | Historical | Alerts
        | Geocoding | Elevation | Marine | Ensemble | Climate | Flood
}