using Domain.Entities.ValueObjects;
using UseCases.Handlers.Queries;
using Shared.Contracts.Queries;

namespace Veder.Tests;

public sealed class HandlerMappingTests
{
    [Fact]
    public async Task GetWeatherForecastQueryHandler_MapsDomainIntoResponse()
    {
        var location = Coordinates.FromDegrees(51.5, -0.12);
        var selector = new FakeProviderSelector(Sample.Forecast(location, "open-meteo"), "open-meteo", fromCache: true);
        var handler = new GetWeatherForecastQueryHandler(selector);

        var result = await handler.Handle(new GetWeatherForecastQuery(location, null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("open-meteo", result.Value.ProviderId);
        Assert.Equal(20, result.Value.TemperatureC);
        Assert.Equal(68, result.Value.TemperatureF);
        Assert.Equal("clear", result.Value.Summary);
        Assert.Equal(50, result.Value.Humidity);
        Assert.Equal(180, result.Value.WindDirection);
        Assert.Equal(location, result.Value.Location);
        Assert.Equal(1, selector.WeatherCalls);
    }
}
