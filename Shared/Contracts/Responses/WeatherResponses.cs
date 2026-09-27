using Domain.Entities.ValueObjects;

namespace Shared.Contracts.Responses;

public sealed record WeatherForecastResponse(
    Coordinates Location,
    double TemperatureC,
    double TemperatureF,
    DateTimeOffset ValidAt,
    string Summary,
    double Humidity,
    double WindSpeed,
    int WindDirection,
    double Pressure,
    double PrecipitationProbability,
    string ProviderId
);

public sealed record AirQualityResponse(
    Coordinates Location,
    int AqiValue,
    string AqiCategory,
    string DominantPollutant,
    DateTimeOffset MeasuredAt,
    Dictionary<string, double> Pollutants,
    string ProviderId
);

public sealed record ProviderResponse(
    string Id,
    string DisplayName,
    string[] Capabilities,
    int Priority,
    bool IsEnabled,
    DateTimeOffset? ApiKeyExpiresAt,
    int? DailyQuota,
    int UsedQuota,
    bool IsHealthy
);

public sealed record SavedLocationResponse(
    string Id,
    string UserId,
    string Label,
    Coordinates Location
);