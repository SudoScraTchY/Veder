using System.Net;
using System.Text;
using Domain.Entities.Exceptions;
using Domain.Entities.ValueObjects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Json;
using Infrastructure.Providers;
using Infrastructure.Providers.OpenMeteo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Veder.Tests;

/// <summary>Owns a throw-away provider store so persistence tests never touch the user's real data.</summary>
public sealed class TempProviderStore : IDisposable
{
    public TempProviderStore()
    {
        Root = Path.Combine(Path.GetTempPath(), "veder-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public ProviderStoreOptions Options => new() { RootPath = Root };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file must not fail the test run; the OS cleans the temp directory eventually.
        }
    }
}

public sealed class OpenMeteoRequestBuilderTests
{
    private static readonly Coordinates Berlin = Coordinates.FromDegrees(52.52, 13.41);
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static OpenMeteoOptions Options => new() { Timezone = "auto", ForecastDays = 3 };

    [Fact]
    public void Build_RendersUnixTimeAndExpectedGroups()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Forecast,
            Location = Berlin,
            Groups = new Dictionary<string, IReadOnlyList<string>>
            {
                ["current"] = ["temperature_2m"],
                ["daily"] = ["temperature_2m_max"]
            }
        };

        var built = OpenMeteoRequestBuilder.Build(request, Options, Today);

        Assert.Equal("forecast", built.Operation);
        Assert.Contains("timeformat=unixtime", built.Url);
        Assert.Contains("current=temperature_2m", built.Url);
        Assert.Contains("daily=temperature_2m_max", built.Url);
        Assert.Contains("forecast_days=3", built.Url);
        Assert.StartsWith(OpenMeteoEndpoints.Forecast, built.Url);
    }

    [Fact]
    public void Build_MasksTheCredentialInTheRecordedUrl()
    {
        var options = Options;
        options.ApiKey = "secret-key-123";

        var built = OpenMeteoRequestBuilder.Build(
            new OpenMeteoRequest
            {
                Dataset = OpenMeteoDataset.Forecast,
                Location = Berlin,
                Groups = new Dictionary<string, IReadOnlyList<string>> { ["current"] = ["temperature_2m"] }
            },
            options,
            Today);

        Assert.Contains("apikey=secret-key-123", built.Url);
        Assert.DoesNotContain("secret-key-123", built.SanitizedUrl);
        Assert.Contains("apikey=***", built.SanitizedUrl);
    }

    [Fact]
    public void Build_RejectsAnUnknownVariable()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Forecast,
            Location = Berlin,
            Groups = new Dictionary<string, IReadOnlyList<string>> { ["current"] = ["not_a_real_variable"] }
        };

        var error = Assert.Throws<ProviderRequestException>(() => OpenMeteoRequestBuilder.Build(request, Options, Today));

        Assert.Contains("not_a_real_variable", error.Reason);
    }

    [Fact]
    public void Build_RejectsAGroupingTheDatasetDoesNotAccept()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Climate,
            Location = Berlin,
            Groups = new Dictionary<string, IReadOnlyList<string>> { ["hourly"] = ["temperature_2m"] }
        };

        var error = Assert.Throws<ProviderRequestException>(() => OpenMeteoRequestBuilder.Build(request, Options, Today));

        Assert.Contains("does not accept", error.Reason);
    }

    [Fact]
    public void Build_RequiresADateRangeForDatedDatasets()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Archive,
            Location = Berlin,
            Groups = new Dictionary<string, IReadOnlyList<string>> { ["daily"] = ["temperature_2m_max"] }
        };

        var error = Assert.Throws<ProviderRequestException>(() => OpenMeteoRequestBuilder.Build(request, Options, Today));

        Assert.Contains("requires both StartDate and EndDate", error.Reason);
    }

    [Fact]
    public void Build_RejectsAnArchiveRangeInsideTheReanalysisLag()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Archive,
            Location = Berlin,
            StartDate = Today.AddDays(-2),
            EndDate = Today,
            Groups = new Dictionary<string, IReadOnlyList<string>> { ["daily"] = ["temperature_2m_max"] }
        };

        var error = Assert.Throws<ProviderRequestException>(() => OpenMeteoRequestBuilder.Build(request, Options, Today));

        Assert.Contains("beyond the latest available date", error.Reason);
    }

    [Fact]
    public void Build_RejectsADateRangeOnAForecastShapedDataset()
    {
        var request = new OpenMeteoRequest
        {
            Dataset = OpenMeteoDataset.Marine,
            Location = Berlin,
            StartDate = Today.AddDays(-3),
            EndDate = Today,
            Groups = new Dictionary<string, IReadOnlyList<string>> { ["hourly"] = ["wave_height"] }
        };

        var error = Assert.Throws<ProviderRequestException>(() => OpenMeteoRequestBuilder.Build(request, Options, Today));

        Assert.Contains("does not accept a date range", error.Reason);
    }
}

public sealed class OpenMeteoParserTests
{
    private const string ForecastPayload = """
    {
      "latitude": 52.52, "longitude": 13.42, "generationtime_ms": 0.5, "utc_offset_seconds": 7200,
      "timezone": "Europe/Berlin", "timezone_abbreviation": "GMT+2", "elevation": 38.0,
      "current_units": { "time": "iso8601", "temperature_2m": "°C", "weather_code": "wmo code" },
      "current": { "time": 1758456000, "temperature_2m": 13.9, "relative_humidity_2m": 66, "wind_speed_10m": 11.8, "wind_direction_10m": 293, "pressure_msl": 1025.2, "weather_code": 3 },
      "hourly_units": { "time": "unixtime", "precipitation_probability": "%" },
      "hourly": { "time": [1758398400, 1758402000], "precipitation_probability": [10, 25] },
      "daily": { "time": [1758412800], "temperature_2m_max": [19.4] }
    }
    """;

    [Fact]
    public void Parse_KeepsTheCurrentObservationOutOfTheSeries()
    {
        var envelope = OpenMeteoResponseParser.Parse(
            ForecastPayload, "open-meteo", "forecast", Coordinates.FromDegrees(52.52, 13.41),
            ["current", "hourly", "daily"], DateTimeOffset.UnixEpoch);

        Assert.NotNull(envelope.Current);
        Assert.Equal(13.9, envelope.Current!.Values["temperature_2m"]);

        // The hourly point is earlier than "now" and must not be mistaken for current conditions.
        Assert.DoesNotContain(envelope.Series.Points, p => p.Time == envelope.Current.Time);
        Assert.All(envelope.Series.Points, p => Assert.True(p.Time < envelope.Current.Time));
        Assert.Equal(3, envelope.Series.Points.Count);
    }

    [Fact]
    public void Parse_ZipsParallelArraysAndMergesUnits()
    {
        var envelope = OpenMeteoResponseParser.Parse(
            ForecastPayload, "open-meteo", "forecast", Coordinates.FromDegrees(52.52, 13.41),
            ["current", "hourly", "daily"], DateTimeOffset.UnixEpoch);

        var hourly = envelope.Series.Points.Where(p => p.Values.ContainsKey("precipitation_probability")).ToList();

        Assert.Equal(2, hourly.Count);
        Assert.Equal(10, hourly[0].Values["precipitation_probability"]);
        Assert.Equal(25, hourly[1].Values["precipitation_probability"]);
        Assert.Equal("Europe/Berlin", envelope.Series.Timezone);
        Assert.Equal(7200, envelope.Series.UtcOffsetSeconds);
        Assert.Equal("°C", envelope.Series.Units["temperature_2m"]);
        Assert.Equal(38.0, envelope.Series.ElevationMeters);
    }

    [Fact]
    public void Parse_SurfacesAProviderErrorPayloadAsARequestFailure()
    {
        const string error = """{ "error": true, "reason": "Invalid date format" }""";

        var exception = Assert.Throws<ProviderRequestException>(() => OpenMeteoResponseParser.Parse(
            error, "open-meteo", "archive", Coordinates.FromDegrees(1, 1), ["daily"], DateTimeOffset.UnixEpoch));

        Assert.Equal("Invalid date format", exception.Reason);
    }

    [Fact]
    public void Parse_RejectsNonJson()
    {
        Assert.Throws<OpenMeteoResponseException>(() => OpenMeteoResponseParser.Parse(
            "<html>gateway error</html>", "open-meteo", "forecast", Coordinates.FromDegrees(1, 1), ["current"], DateTimeOffset.UnixEpoch));
    }
}

public sealed class OpenMeteoMapperTests
{
    private static readonly Coordinates Berlin = Coordinates.FromDegrees(52.52, 13.41);

    private static OpenMeteoEnvelope EnvelopeFor(string json, params string[] groups) =>
        OpenMeteoResponseParser.Parse(json, "open-meteo", "forecast", Berlin, groups, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ToWeatherForecast_UsesTheCurrentObservation()
    {
        var envelope = EnvelopeFor("""
        { "utc_offset_seconds": 0, "current": { "time": 1758456000, "temperature_2m": 20, "relative_humidity_2m": 55,
          "wind_speed_10m": 12, "wind_direction_10m": 180, "pressure_msl": 1013, "weather_code": 61 } }
        """, "current");

        var forecast = OpenMeteoMapper.ToWeatherForecast(envelope, Berlin);

        Assert.Equal(20, forecast.Temperature.Celsius);
        Assert.Equal(68, forecast.Temperature.Fahrenheit);
        Assert.Equal(55, forecast.Humidity);
        Assert.Equal(12, forecast.WindSpeed);
        Assert.Equal(180, forecast.WindDirection);
        Assert.Equal(1013, forecast.Pressure);
        Assert.Equal("Slight rain", forecast.Summary);
        Assert.Equal("open-meteo", forecast.ProviderId);
    }

    [Fact]
    public void DescribeWeatherCode_ReportsUnknownCodesRatherThanGuessing()
    {
        Assert.Equal("Overcast", OpenMeteoMapper.DescribeWeatherCode(3));
        Assert.Equal("WMO code 123", OpenMeteoMapper.DescribeWeatherCode(123));
        Assert.Equal("Unknown conditions", OpenMeteoMapper.DescribeWeatherCode(null));
    }

    [Fact]
    public void ToAirQualityReading_PrefersEuropeanAqiAndKeepsEveryPollutant()
    {
        var envelope = EnvelopeFor("""
        { "utc_offset_seconds": 0, "current": { "time": 1758456000, "european_aqi": 20, "us_aqi": 41,
          "pm2_5": 6.4, "pm10": 9.1, "birch_pollen": 0.5 } }
        """, "current");

        var reading = OpenMeteoMapper.ToAirQualityReading(envelope, Berlin);

        Assert.Equal(20, reading.Index.Value);
        Assert.Equal("Good", reading.Index.Category);
        Assert.Equal(3, reading.Pollutants.Count);
        Assert.False(reading.Pollutants.ContainsKey("european_aqi"));
    }

    [Fact]
    public void ToWeatherForecast_WithoutACurrentBlock_Fails()
    {
        var envelope = EnvelopeFor("""{ "utc_offset_seconds": 0, "hourly": { "time": [1758398400], "temperature_2m": [10] } }""", "hourly");

        Assert.Throws<OpenMeteoResponseException>(() => OpenMeteoMapper.ToWeatherForecast(envelope, Berlin));
    }
}

public sealed class OpenMeteoClientFailureTests
{
    private static (OpenMeteoClient Client, TempProviderStore Store) Build(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        var store = new TempProviderStore();
        var handler = new StubHandler(status, body, retryAfter);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.open-meteo.com") };
        var options = Options.Create(new OpenMeteoOptions { TimeoutSeconds = 5, RecordCalls = true });
        var recorder = new ProviderCallRecorder(
            new JsonProviderCallRecordStore(Options.Create(store.Options), NullLogger<JsonProviderCallRecordStore>.Instance),
            new JsonProviderDiagnosticsStore(Options.Create(store.Options)),
            new JsonObservationStore(Options.Create(store.Options)),
            NullLogger<ProviderCallRecorder>.Instance);

        return (new OpenMeteoClient(http, options, new OpenMeteoTelemetry(), NullLogger<OpenMeteoClient>.Instance, recorder, new ProviderConcurrencyGate(4)), store);
    }

    private static OpenMeteoRequest Request() => new()
    {
        Dataset = OpenMeteoDataset.Forecast,
        Location = Coordinates.FromDegrees(52.52, 13.41),
        Groups = new Dictionary<string, IReadOnlyList<string>> { ["current"] = ["temperature_2m"] }
    };

    [Fact]
    public async Task BadRequest_BecomesARequestExceptionCarryingTheProviderReason()
    {
        var (client, store) = Build(HttpStatusCode.BadRequest, """{ "error": true, "reason": "Latitude must be in range" }""");
        using var _ = store;

        var error = await Assert.ThrowsAsync<ProviderRequestException>(
            () => client.GetSeriesAsync(Request(), CancellationToken.None));

        Assert.Equal("Latitude must be in range", error.Reason);
        var record = (await store.RecordStore().GetByProviderAsync("open-meteo", 10, CancellationToken.None)).Single();
        Assert.Equal("request-rejected", record.Outcome);
        Assert.Equal(400, record.HttpStatus);
    }

    [Fact]
    public async Task Unauthorized_BecomesAnAuthenticationException()
    {
        var (client, store) = Build(HttpStatusCode.Unauthorized, """{ "error": true, "reason": "Invalid API key" }""");
        using var _ = store;

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => client.GetSeriesAsync(Request(), CancellationToken.None));

        Assert.Contains("Invalid API key", error.Detail);
    }

    [Fact]
    public async Task TooManyRequests_BecomesARateLimitExceptionHonouringRetryAfter()
    {
        var (client, store) = Build(HttpStatusCode.TooManyRequests, """{ "error": true, "reason": "Minutely API request limit exceeded" }""", TimeSpan.FromSeconds(30));
        using var _ = store;

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => client.GetSeriesAsync(Request(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);

        var diagnostics = await store.DiagnosticsStore().GetAsync("open-meteo", CancellationToken.None);
        Assert.Equal("rate-limited", diagnostics!.LastFailureOutcome);
        Assert.Equal(1, diagnostics.ConsecutiveFailures);
    }

    [Fact]
    public async Task ServerError_BecomesAnUnavailableException()
    {
        var (client, store) = Build(HttpStatusCode.ServiceUnavailable, "upstream maintenance");
        using var _ = store;

        await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => client.GetSeriesAsync(Request(), CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? retryAfter) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };

            if (retryAfter is { } wait)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            }

            return Task.FromResult(response);
        }
    }
}

public sealed class ProviderStoreTests
{
    private static readonly TempProviderStore Store = new();

    private static JsonProviderCallRecordStore Records(TempProviderStore store) =>
        new(Options.Create(store.Options), NullLogger<JsonProviderCallRecordStore>.Instance);

    [Fact]
    public async Task AppendAndQuery_RoundTripsRecordsNewestFirst()
    {
        using var store = new TempProviderStore();
        var records = Records(store);

        await records.AppendAsync(NewRecord("forecast", DateTimeOffset.UnixEpoch), CancellationToken.None);
        await records.AppendAsync(NewRecord("air-quality", DateTimeOffset.UnixEpoch.AddMinutes(5)), CancellationToken.None);

        var all = await records.GetRecentAsync(10, CancellationToken.None);
        var filtered = await records.GetByProviderAsync("open-meteo", 10, CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal("air-quality", all[0].Operation);
        Assert.Equal(2, filtered.Count);
        Assert.Equal(2, await records.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Diagnostics_TrackTheLastSuccessAndFailureAndConsecutiveFailures()
    {
        using var store = new TempProviderStore();
        var diagnostics = new JsonProviderDiagnosticsStore(Options.Create(store.Options));

        await diagnostics.RecordSuccessAsync("open-meteo", DateTimeOffset.UnixEpoch, "forecast", CancellationToken.None);
        await diagnostics.RecordFailureAsync("open-meteo", DateTimeOffset.UnixEpoch.AddMinutes(1), "archive", "rate-limited", "slow down", CancellationToken.None);
        await diagnostics.RecordFailureAsync("open-meteo", DateTimeOffset.UnixEpoch.AddMinutes(2), "archive", "timeout", "took too long", CancellationToken.None);

        var view = await diagnostics.GetAsync("open-meteo", CancellationToken.None);

        Assert.Equal("forecast", view!.LastSuccessOperation);
        Assert.Equal("timeout", view.LastFailureOutcome);
        Assert.Equal(2, view.ConsecutiveFailures);
        Assert.Equal(1, view.TotalSuccesses);
        Assert.Equal(2, view.TotalFailures);

        await diagnostics.RecordSuccessAsync("open-meteo", DateTimeOffset.UnixEpoch.AddMinutes(3), "forecast", CancellationToken.None);
        view = await diagnostics.GetAsync("open-meteo", CancellationToken.None);
        Assert.Equal(0, view!.ConsecutiveFailures);
    }

    [Fact]
    public async Task Manifest_IsWrittenAndRejectedWhenNewerThanTheBuild()
    {
        using var store = new TempProviderStore();
        var records = Records(store);
        await records.AppendAsync(NewRecord("forecast", DateTimeOffset.UnixEpoch), CancellationToken.None);

        var manifestPath = Path.Combine(store.Root, ProviderStoreManifest.FileName);
        Assert.True(File.Exists(manifestPath));
        Assert.Contains($"\"schemaVersion\": {ProviderStoreManifest.CurrentVersion}", await File.ReadAllTextAsync(manifestPath));

        await File.WriteAllTextAsync(manifestPath, """{ "schemaVersion": 99, "createdAt": "2026-01-01T00:00:00+00:00", "updatedAt": "2026-01-01T00:00:00+00:00" }""");

        var fresh = new JsonProviderCallRecordStore(Options.Create(store.Options), NullLogger<JsonProviderCallRecordStore>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.AppendAsync(NewRecord("forecast", DateTimeOffset.UnixEpoch), CancellationToken.None));
    }

    private static Domain.Entities.ProviderCallRecord NewRecord(string operation, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        ProviderId = "open-meteo",
        Operation = operation,
        SanitizedUrl = "https://api.open-meteo.com/v1/forecast?latitude=1&longitude=1",
        StartedAt = at,
        DurationMs = 42,
        Outcome = "success",
        HttpStatus = 200,
        ResponseBytes = 1024,
        PointCount = 24
    };
}

internal static class TempProviderStoreExtensions
{
    public static JsonProviderCallRecordStore RecordStore(this TempProviderStore store) =>
        new(Options.Create(store.Options), NullLogger<JsonProviderCallRecordStore>.Instance);

    public static JsonProviderDiagnosticsStore DiagnosticsStore(this TempProviderStore store) =>
        new(Options.Create(store.Options));
}
