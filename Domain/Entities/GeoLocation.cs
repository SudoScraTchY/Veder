using Domain.Entities.ValueObjects;

namespace Domain.Entities;

public sealed record GeoLocation(
    long Id,
    string Name,
    Coordinates Location,
    double? ElevationMeters,
    string? CountryCode,
    string? Country,
    string? Admin1,
    string? Timezone,
    long? Population,
    string? FeatureCode);
