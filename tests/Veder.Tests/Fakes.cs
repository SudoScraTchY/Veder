using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Veder.Tests;

internal static class Sample
{
    public static WeatherForecast Forecast(Coordinates location, string providerId) => new(
        Location: location,
        Temperature: new Temperature(20),
        ValidAt: DateTimeOffset.UnixEpoch,
        Summary: "clear",
        Humidity: 50,
        WindSpeed: 5,
        WindDirection: 180,
        Pressure: 1013,
        PrecipitationProbability: 0,
        SourceCapabilities: ProviderCapability.CurrentWeather | ProviderCapability.Forecast,
        ProviderId: providerId);

    public static AirQualityReading AirQuality(Coordinates location, string providerId) => new(
        Location: location,
        Index: new AirQualityIndex(42, "Good", "PM2.5"),
        MeasuredAt: DateTimeOffset.UnixEpoch,
        Pollutants: new Dictionary<string, double> { ["pm2_5"] = 12.5 },
        SourceCapabilities: ProviderCapability.AirQuality,
        ProviderId: providerId);
}

internal sealed class FakeWeatherProvider(string providerId, Exception? throwOnCall = null) : IWeatherProvider
{
    public string ProviderId { get; } = providerId;

    public Exception? ThrowOnCall { get; set; } = throwOnCall;

    public int Calls { get; private set; }

    public Task<WeatherForecast> GetForecastAsync(Coordinates location, CancellationToken ct)
    {
        Calls++;
        return ThrowOnCall is null
            ? Task.FromResult(Sample.Forecast(location, ProviderId))
            : Task.FromException<WeatherForecast>(ThrowOnCall);
    }
}

internal sealed class FakeAirQualityProvider(string providerId, Exception? throwOnCall = null) : IAirQualityProvider
{
    public string ProviderId { get; } = providerId;

    public Exception? ThrowOnCall { get; set; } = throwOnCall;

    public int Calls { get; private set; }

    public Task<AirQualityReading> GetCurrentAsync(Coordinates location, CancellationToken ct)
    {
        Calls++;
        return ThrowOnCall is null
            ? Task.FromResult(Sample.AirQuality(location, ProviderId))
            : Task.FromException<AirQualityReading>(ThrowOnCall);
    }
}

/// <summary>Records whether the selector asked for the cache path and keeps call counts observable.</summary>
internal sealed class FakeProviderSelector(WeatherForecast forecast, string providerId, bool fromCache) : IProviderSelector
{
    public int WeatherCalls { get; private set; }

    public Task<(WeatherForecast Forecast, string ProviderId, bool FromCache)> ResolveWeatherAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct)
    {
        WeatherCalls++;
        return Task.FromResult((forecast, providerId, fromCache));
    }

    public Task<(AirQualityReading Reading, string ProviderId, bool FromCache)> ResolveAirQualityAsync(
        Coordinates location, string? requestedProviderId, CancellationToken ct)
        => throw new NotSupportedException();
}
