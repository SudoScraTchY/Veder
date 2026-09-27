using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;
using Cortex.Mediator.Queries;
using ErrorOr;

namespace UseCases.Handlers.Queries;

public sealed class GetWeatherForecastQueryHandler : IQueryHandler<GetWeatherForecastQuery, ErrorOr<WeatherForecastResponse>>
{
    private readonly IProviderSelector _providerSelector;

    public GetWeatherForecastQueryHandler(IProviderSelector providerSelector)
    {
        _providerSelector = providerSelector;
    }

    public async Task<ErrorOr<WeatherForecastResponse>> Handle(GetWeatherForecastQuery query, CancellationToken ct)
    {
        var (forecast, providerId, fromCache) = await _providerSelector.ResolveWeatherAsync(query.Location, query.ProviderId, ct);

        return new WeatherForecastResponse(
            Location: forecast.Location,
            TemperatureC: forecast.Temperature.Celsius,
            TemperatureF: forecast.Temperature.Fahrenheit,
            ValidAt: forecast.ValidAt,
            Summary: forecast.Summary,
            Humidity: forecast.Humidity,
            WindSpeed: forecast.WindSpeed,
            WindDirection: forecast.WindDirection,
            Pressure: forecast.Pressure,
            PrecipitationProbability: forecast.PrecipitationProbability,
            ProviderId: providerId
        );
    }
}