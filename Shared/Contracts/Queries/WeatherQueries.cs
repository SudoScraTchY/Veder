using Domain.Entities.ValueObjects;
using Cortex.Mediator.Queries;
using ErrorOr;
using Shared.Contracts.Responses;

namespace Shared.Contracts.Queries;

public sealed record GetWeatherForecastQuery(Coordinates Location, string? ProviderId = null) : IQuery<ErrorOr<WeatherForecastResponse>>;

public sealed record GetAirQualityQuery(Coordinates Location, string? ProviderId = null) : IQuery<ErrorOr<AirQualityResponse>>;

public sealed record GetAvailableProvidersQuery : IQuery<ErrorOr<IReadOnlyList<ProviderResponse>>>;

public sealed record GetSavedLocationsQuery(string UserId) : IQuery<ErrorOr<IReadOnlyList<SavedLocationResponse>>>;