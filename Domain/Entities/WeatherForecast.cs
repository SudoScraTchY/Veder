using Domain.Entities.ValueObjects;
using Domain.Entities.Enumerations;

namespace Domain.Entities;

public sealed record WeatherForecast(
    Coordinates Location,
    Temperature Temperature,
    DateTimeOffset ValidAt,
    string Summary,
    double Humidity,
    double WindSpeed,
    int WindDirection,
    double Pressure,
    double PrecipitationProbability,
    ProviderCapability SourceCapabilities,
    string ProviderId
);