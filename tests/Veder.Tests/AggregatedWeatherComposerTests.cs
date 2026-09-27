using Domain.Caching;
using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.ValueObjects;
using Shared.Contracts.Responses;

namespace Veder.Tests;

/// <summary>
/// The aggregated response is the WebApp's contract, so its provenance honesty and its willingness to
/// return a partial payload are pinned here rather than discovered in the browser.
/// </summary>
public sealed class AggregatedWeatherComposerTests
{
    private static readonly Coordinates Tehran = Coordinates.FromDegrees(35.6892, 51.3890);
    private static readonly Coordinates Nearby = Coordinates.FromDegrees(35.6895, 51.3893);

    private static WeatherForecast Forecast(double celsius = 22.4) => new(
        Location: Tehran,
        Temperature: Temperature.FromCelsius(celsius),
        ValidAt: DateTimeOffset.UnixEpoch.AddHours(5),
        Summary: "Clear sky",
        Humidity: 31.6,
        WindSpeed: 12.34,
        WindDirection: 210,
        Pressure: 1013.27,
        PrecipitationProbability: 0,
        SourceCapabilities: ProviderCapability.CurrentWeather | ProviderCapability.Forecast,
        ProviderId: "open-meteo");

    private static AirQualityReading Air() => new(
        Location: Tehran,
        Index: AirQualityIndex.FromValue(42),
        MeasuredAt: DateTimeOffset.UnixEpoch.AddHours(5),
        Pollutants: new Dictionary<string, double> { ["pm2_5"] = 12.5, ["pm10"] = 20.1 },
        SourceCapabilities: ProviderCapability.AirQuality,
        ProviderId: "open-meteo");

    private static ResolvedLocation Location() => new(
        RequestedName: "Tehran",
        Latitude: Tehran.Latitude,
        Longitude: Tehran.Longitude,
        CountryCode: "IR",
        Country: "Iran",
        Admin1: "Tehran",
        Timezone: "Asia/Tehran",
        CallerDistanceMeters: 0);

    private static CacheReadResult<WeatherForecast> Read(CacheOutcome outcome, bool degraded = false) => new(
        Value: Forecast(),
        SourceProviderId: "open-meteo",
        Outcome: outcome,
        Age: TimeSpan.FromSeconds(120),
        IsStale: degraded);

    [Fact]
    public void HappyPathCarriesLocationCurrentForecastAndAirQuality()
    {
        var response = AggregatedWeatherComposer.Compose(
            Location(),
            new ResponseProvenance(null, "open-meteo", false, false, "Hit", 120, DateTimeOffset.UnixEpoch),
            Forecast(),
            [new DailyForecastItem(new DateOnly(2026, 9, 22), 26.2, 14.8, "Clear sky", 0, 18.5)],
            Air());

        Assert.Empty(response.Warnings);
        Assert.Equal(22.4, response.Current!.TemperatureC);
        Assert.Equal(32, response.Current.HumidityPercent); // 31.6 % rounds to 32
        Assert.Single(response.Forecast);
        Assert.Equal(42, response.AirQuality!.AqiValue);
        Assert.Equal(2, response.AirQuality.PollutantCount);
        Assert.False(response.Provenance.Failover);
        Assert.False(response.Provenance.Degraded);
    }

    [Fact]
    public void AMissingForecastDegradesThatSectionWithoutFailingTheResponse()
    {
        var response = AggregatedWeatherComposer.Compose(
            Location(),
            new ResponseProvenance(null, "open-meteo", false, false, "Failed", 0, DateTimeOffset.UnixEpoch),
            forecast: null,
            daily: null,
            airQuality: Air());

        Assert.Null(response.Current);
        Assert.NotNull(response.AirQuality);
        Assert.Contains("Current conditions are unavailable.", response.Warnings);
    }

    [Fact]
    public void StaleAndFailoverAreStatedAsWarningsRatherThanHidden()
    {
        var response = AggregatedWeatherComposer.Compose(
            Location(),
            new ResponseProvenance("openweathermap", "open-meteo", Failover: true, Degraded: true, "StaleOnProviderFailure", 900, DateTimeOffset.UnixEpoch),
            Forecast(),
            [],
            Air());

        Assert.True(response.Provenance.Failover);
        Assert.True(response.Provenance.Degraded);
        Assert.Contains(response.Warnings, w => w.Contains("openweathermap") && w.Contains("open-meteo"));
        Assert.Contains(response.Warnings, w => w.Contains("stale"));
    }

    [Fact]
    public void AirQualityCanBeSuppressedEntirelyWithoutAWarning()
    {
        var response = AggregatedWeatherComposer.Compose(
            Location(),
            new ResponseProvenance(null, "open-meteo", false, false, "Hit", 0, DateTimeOffset.UnixEpoch),
            Forecast(),
            [],
            airQuality: null,
            includeAirQuality: false);

        Assert.Null(response.AirQuality);
        Assert.DoesNotContain(response.Warnings, w => w.Contains("Air quality"));
    }

    [Fact]
    public void ProvenanceIsDerivedFromTheCacheRead()
    {
        var provenance = AggregatedWeatherComposer.ProvenanceFrom(
            "openweathermap",
            Read(CacheOutcome.StaleOnProviderFailure, degraded: true),
            DateTimeOffset.UnixEpoch);

        Assert.Equal("openweathermap", provenance.RequestedProvider);
        Assert.Equal("open-meteo", provenance.SourceProvider);
        Assert.True(provenance.Failover);
        Assert.True(provenance.Degraded);
        Assert.Equal("StaleOnProviderFailure", provenance.CacheOutcome);
        Assert.Equal(120, provenance.AgeSeconds);
    }

    [Fact]
    public void NonDegradedReadFromThePreferredProviderIsNotAFailover()
    {
        var provenance = AggregatedWeatherComposer.ProvenanceFrom("open-meteo", Read(CacheOutcome.Hit), DateTimeOffset.UnixEpoch);

        Assert.False(provenance.Failover);
        Assert.False(provenance.Degraded);
    }

    [Fact]
    public void NearbyCoordinatesReportASmallDistance()
    {
        var distance = AggregatedWeatherComposer.DistanceMeters(Tehran, Nearby);

        Assert.InRange(distance, 1, 60);
        Assert.Equal(0, AggregatedWeatherComposer.DistanceMeters(Tehran, Tehran));
    }

    [Fact]
    public void DistanceIsSaneAtCityScale()
    {
        // Tehran to Karaj is roughly 40 km; this guards the haversine against a degrees/radians slip.
        var karaj = Coordinates.FromDegrees(35.84, 50.94);

        Assert.InRange(AggregatedWeatherComposer.DistanceMeters(Tehran, karaj), 35_000, 46_000);
    }
}
