using System.Net;
using System.Net.Http.Json;
using System.Text;
using Infrastructure.Providers.OpenMeteo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Responses;

namespace Veder.Tests;

/// <summary>
/// Boots the real WebApi pipeline with a stubbed network, so the weather routes are proven to flow
/// through the mediator, the provider selector, the registry and the Open-Meteo adapter without
/// depending on the public API being reachable from CI.
/// </summary>
public sealed class WeatherEndpointTests
{
    private const string ForecastPayload = """
    {
      "latitude": 52.52, "longitude": 13.42, "generationtime_ms": 0.4, "utc_offset_seconds": 7200,
      "timezone": "Europe/Berlin", "timezone_abbreviation": "GMT+2", "elevation": 38.0,
      "current_units": { "temperature_2m": "°C", "relative_humidity_2m": "%" },
      "current": { "time": 1758456000, "temperature_2m": 13.9, "relative_humidity_2m": 66, "apparent_temperature": 12.1,
        "precipitation": 0, "weather_code": 3, "cloud_cover": 90, "pressure_msl": 1025.2,
        "wind_speed_10m": 11.8, "wind_direction_10m": 293, "wind_gusts_10m": 22.0 },
      "hourly_units": { "precipitation_probability": "%", "temperature_2m": "°C" },
      "hourly": { "time": [1758452400, 1758456000], "precipitation_probability": [10, 25], "temperature_2m": [13.1, 13.9] },
      "daily_units": { "temperature_2m_max": "°C" },
      "daily": { "time": [1758412800], "temperature_2m_max": [19.4], "temperature_2m_min": [8.2], "weather_code": [3],
        "sunrise": [1758430800], "sunset": [1758477000], "precipitation_sum": [0.0] }
    }
    """;

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    private static (HttpClient Client, StubHandler Stub) NewClient(string payload = ForecastPayload)
    {
        var stub = new StubHandler(payload);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
                services.AddHttpClient(OpenMeteoProviderConstants.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => stub));
        });

        return (factory.CreateClient(), stub);
    }

    [Fact]
    public async Task Forecast_FlowsThroughTheSelectorIntoTheOpenMeteoAdapter()
    {
        var (client, stub) = NewClient();

        var response = await client.GetAsync("/api/weather/forecast?lat=52.52&lon=13.41");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<WeatherForecastResponse>();

        Assert.NotNull(body);
        Assert.Equal("open-meteo", body!.ProviderId);
        Assert.Equal(13.9, body.TemperatureC);
        Assert.Equal(66, body.Humidity);
        Assert.Equal("Overcast", body.Summary);
        Assert.Equal(1, stub.Calls);
        Assert.Contains("api.open-meteo.com/v1/forecast", stub.LastRequestUri!.ToString());
        Assert.Contains("timeformat=unixtime", stub.LastRequestUri.Query);
    }

    [Fact]
    public async Task AirQuality_FlowsThroughTheSelectorAsWell()
    {
        var (client, stub) = NewClient("""
        { "utc_offset_seconds": 0, "current_units": { "european_aqi": "EAQI" },
          "current": { "time": 1758456000, "european_aqi": 18, "us_aqi": 39, "pm2_5": 4.6, "pm10": 10.7 },
          "hourly": { "time": [1758452400], "pm2_5": [4.4], "european_aqi": [17] } }
        """);

        var response = await client.GetAsync("/api/weather/aqi?lat=52.52&lon=13.41");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AirQualityResponse>();

        Assert.NotNull(body);
        Assert.Equal(18, body!.AqiValue);
        Assert.Equal("Good", body.AqiCategory);
        Assert.Equal("open-meteo", body.ProviderId);
        Assert.Contains("air-quality", stub.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task Forecast_IsServedFromCacheOnTheSecondCall()
    {
        var (client, stub) = NewClient();

        await client.GetAsync("/api/weather/forecast?lat=52.52&lon=13.41");
        await client.GetAsync("/api/weather/forecast?lat=52.52&lon=13.41");

        // The selector caches per provider+location, so the provider is only called once.
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task InvalidCoordinates_ReturnA400Problem()
    {
        var (client, stub) = NewClient();

        var response = await client.GetAsync("/api/weather/forecast?lat=999&lon=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("InvalidCoordinates", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task ProviderRejection_IsMappedTo400BeforeAnyNetworkCall()
    {
        var (client, stub) = NewClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.GetAsync(
            $"/api/weather/archive?lat=52.52&lon=13.41&start={today.AddDays(-1):yyyy-MM-dd}&end={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ProviderRejectedRequest", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, stub.Calls);
    }
}
