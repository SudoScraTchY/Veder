using Domain.Entities.ValueObjects;
using Domain.Entities.Enumerations;

namespace Domain.Entities;

public sealed record AirQualityReading(
    Coordinates Location,
    AirQualityIndex Index,
    DateTimeOffset MeasuredAt,
    Dictionary<string, double> Pollutants,
    ProviderCapability SourceCapabilities,
    string ProviderId
);