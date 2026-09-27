using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Infrastructure.Helpers.DI;
using Infrastructure.Persistence;
using Infrastructure.Providers.OpenMeteo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veder.ProviderProbe;

/// <summary>
/// End-to-end demonstration of the Open-Meteo provider: it drives every implemented capability
/// through the product's own code path (options → client → adapters → mapper → durable record
/// store) against the public API, then prints the records it left behind.
/// </summary>
public static class Program
{
    private static readonly Coordinates Berlin = Coordinates.FromDegrees(52.52, 13.41);
    private static readonly Coordinates Hamburg = Coordinates.FromDegrees(53.55, 9.99);
    private static readonly Coordinates NorthSea = Coordinates.FromDegrees(54.0, 7.0);
    private static int _failures;

    public static async Task<int> Main(string[] args)
    {
        var storeRoot = Environment.GetEnvironmentVariable("VEDER_PROVIDER_STORE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Veder", "provider-store");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenMeteo:Timezone"] = "auto",
                ["OpenMeteo:ForecastDays"] = "2",
                ["OpenMeteo:RecordCalls"] = "true",
                ["OpenMeteo:MaxRetryAttempts"] = "2",
                ["ProviderStore:RootPath"] = storeRoot
            })
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new ProbeLoggerProvider());
        });
        services.AddOpenMeteoProvider(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var options = provider.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
        var weather = provider.GetRequiredService<OpenMeteoWeatherProvider>();
        var airQuality = provider.GetRequiredService<OpenMeteoAirQualityProvider>();
        var extended = provider.GetRequiredService<OpenMeteoExtendedProvider>();
        var records = provider.GetRequiredService<IProviderCallRecordStore>();
        var diagnostics = provider.GetRequiredService<IProviderDiagnosticsStore>();

        var startedAt = DateTimeOffset.UtcNow;

        // Operator verbs: exercise the credential lifecycle and inspect synced observations.
        if (args.Length > 0 && string.Equals(args[0], "credentials", StringComparison.OrdinalIgnoreCase))
        {
            return await CredentialsAsync(provider, args.Skip(1).ToArray());
        }

        if (args.Length > 0 && string.Equals(args[0], "observations", StringComparison.OrdinalIgnoreCase))
        {
            return await ObservationsAsync(provider);
        }

        Console.WriteLine("Veder provider probe — Open-Meteo");
        Console.WriteLine($"  {options.Describe()}");
        Console.WriteLine($"  record store: {storeRoot}");
        Console.WriteLine();

        await RunAsync("weather forecast (current + hourly + daily)", async () =>
        {
            var forecast = await weather.GetForecastAsync(Berlin, CancellationToken.None);
            return $"{forecast.Summary}; {forecast.Temperature.Celsius:F1} °C (feels {forecast.Temperature.Fahrenheit:F0} °F), " +
                   $"humidity {forecast.Humidity:F0}%, wind {forecast.WindSpeed:F1} km/h from {forecast.WindDirection}°, " +
                   $"pressure {forecast.Pressure:F0} hPa";
        });

        await RunAsync("weather forecast series (full hourly/daily arrays)", async () =>
        {
            var series = await weather.GetForecastSeriesAsync(Berlin, CancellationToken.None);
            return $"{series.Points.Count} points, {series.Units.Count} unit entries, tz={series.Timezone}, elevation={series.ElevationMeters:F0} m";
        });

        await RunAsync("air quality (AQI + pollutants + pollen)", async () =>
        {
            var reading = await airQuality.GetCurrentAsync(Berlin, CancellationToken.None);
            return $"AQI {reading.Index.Value} ({reading.Index.Category}), dominant {reading.Index.DominantPollutant}, " +
                   $"{reading.Pollutants.Count} pollutants measured";
        });

        await RunAsync("air quality series", async () =>
        {
            var series = await airQuality.GetAirQualitySeriesAsync(Berlin, CancellationToken.None);
            return $"{series.Points.Count} points";
        });

        await RunAsync("geocoding search", async () =>
        {
            var places = await extended.SearchAsync("Berlin", 3, "en", CancellationToken.None);
            var first = places.First();
            return $"{places.Count} matches; first: {first.Name} ({first.CountryCode}, {first.Admin1}) " +
                   $"at {first.Location.Latitude:F4},{first.Location.Longitude:F4}, tz={first.Timezone}, population={first.Population}";
        });

        await RunAsync("geocoding by id", async () =>
        {
            var place = await extended.GetByIdAsync(2950159, CancellationToken.None);
            return place is null ? "no match for id 2950159" : $"{place.Name}, {place.Country}, elevation {place.ElevationMeters:F0} m";
        });

        await RunAsync("elevation lookup (2 coordinates)", async () =>
        {
            var elevations = await extended.GetElevationsAsync([Berlin, Hamburg], CancellationToken.None);
            return $"Berlin {elevations[0]:F0} m, Hamburg {elevations[1]:F0} m";
        });

        await RunAsync("historical archive", async () =>
        {
            var end = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
            var start = end.AddDays(-6);
            var series = await extended.GetArchiveAsync(Berlin, start, end, CancellationToken.None);
            var sample = series.Points.First(p => p.Values.ContainsKey("temperature_2m_max"));
            return $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}: {series.Points.Count} points; " +
                   $"first max temp {sample.Values["temperature_2m_max"]:F1} °C";
        });

        await RunAsync("historical forecast", async () =>
        {
            var end = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
            var start = end.AddDays(-4);
            var series = await extended.GetHistoricalForecastAsync(Berlin, start, end, CancellationToken.None);
            return $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}: {series.Points.Count} points";
        });

        await RunAsync("marine (North Sea)", async () =>
        {
            var series = await extended.GetMarineAsync(NorthSea, CancellationToken.None);
            var point = series.Points.FirstOrDefault(p => p.Values.ContainsKey("wave_height"));
            return point is null
                ? $"{series.Points.Count} points (no wave_height sample)"
                : $"{series.Points.Count} points; wave height {point.Values["wave_height"]:F2} m";
        });

        await RunAsync("ensemble", async () =>
        {
            var series = await extended.GetEnsembleAsync(Berlin, CancellationToken.None);
            var memberKeys = series.Points.SelectMany(p => p.Values.Keys).Where(k => k.StartsWith("temperature_2m", StringComparison.Ordinal)).Distinct().Count();
            return $"{series.Points.Count} points across {memberKeys} temperature members";
        });

        await RunAsync("climate projection", async () =>
        {
            var series = await extended.GetClimateProjectionAsync(Berlin, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5), CancellationToken.None);
            return $"{series.Points.Count} daily points for 2024-01-01..2024-01-05";
        });

        await RunAsync("flood", async () =>
        {
            var series = await extended.GetFloodAsync(Berlin, CancellationToken.None);
            var point = series.Points.FirstOrDefault(p => p.Values.ContainsKey("river_discharge"));
            return $"{series.Points.Count} points; river discharge {(point is null ? "n/a" : point.Values["river_discharge"].ToString("F2") + " m³/s")}";
        });

        Console.WriteLine();
        Console.WriteLine("Persisted evidence");
        var all = await records.GetRecentAsync(100, CancellationToken.None);
        var since = all.Where(r => r.StartedAt >= startedAt).ToList();
        Console.WriteLine($"  call records this run : {since.Count} (successful {since.Count(r => r.Succeeded)}, failed {since.Count(r => !r.Succeeded)})");
        Console.WriteLine($"  total records in store: {all.Count}  ({records.GetType().Name})");

        var last = all.FirstOrDefault();
        if (last is not null)
        {
            Console.WriteLine($"  most recent           : {last.Operation} [{last.Outcome}] HTTP {last.HttpStatus} in {last.DurationMs} ms, {last.ResponseBytes} bytes, {last.PointCount} points");
            Console.WriteLine($"  sanitized url         : {last.SanitizedUrl}");
        }

        var openMeteo = await diagnostics.GetAsync(OpenMeteoProviderConstants.ProviderId, CancellationToken.None);
        if (openMeteo is not null)
        {
            Console.WriteLine($"  last success          : {openMeteo.LastSuccessAt:O} ({openMeteo.LastSuccessOperation})");
            Console.WriteLine($"  last failure          : {(openMeteo.LastFailureAt is null ? "none" : $"{openMeteo.LastFailureAt:O} ({openMeteo.LastFailureOperation}/{openMeteo.LastFailureOutcome})")}");
            Console.WriteLine($"  totals                : successes={openMeteo.TotalSuccesses}, failures={openMeteo.TotalFailures}, consecutive failures={openMeteo.ConsecutiveFailures}");
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "RESULT: all Open-Meteo capabilities responded successfully."
            : $"RESULT: {_failures} capability check(s) failed.");

        return _failures == 0 ? 0 : 1;
    }

    private static async Task<int> CredentialsAsync(IServiceProvider provider, string[] args)
    {
        var store = provider.GetRequiredService<IProviderCredentialStore>();
        var verb = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        var providerId = OpenMeteoProviderConstants.ProviderId;

        switch (verb)
        {
            case "status":
            {
                var metadata = await store.GetMetadataAsync(providerId, CancellationToken.None);
                Console.WriteLine(metadata is null
                    ? "No credential stored - the provider runs on the free tier."
                    : $"provider={metadata.ProviderId} fingerprint={metadata.Fingerprint} hint={metadata.Hint} " +
                      $"protection={metadata.Protection} created={metadata.CreatedAt:O} rotated={metadata.RotatedAt:O} " +
                      $"rotations={metadata.RotationCount} revoked={metadata.IsRevoked}");
                return 0;
            }

            case "set":
            case "rotate":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine($"usage: credentials {verb} <secret>");
                    return 2;
                }

                var metadata = await store.StoreAsync(providerId, args[1], CancellationToken.None);
                Console.WriteLine($"{verb}: fingerprint={metadata.Fingerprint} hint={metadata.Hint} rotations={metadata.RotationCount}");
                return 0;
            }

            case "revoke":
            {
                var revoked = await store.RevokeAsync(providerId, CancellationToken.None);
                Console.WriteLine(revoked
                    ? "revoked: the stored secret is gone and the provider falls back to the free tier."
                    : "nothing to revoke.");
                return revoked ? 0 : 1;
            }

            default:
                Console.WriteLine($"unknown verb '{verb}'. Use: credentials status|set <secret>|rotate <secret>|revoke");
                return 2;
        }
    }

    private static async Task<int> ObservationsAsync(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<IObservationStore>();
        var total = await store.CountAsync(CancellationToken.None);
        var newest = await store.GetRecentAsync(null, 10, CancellationToken.None);

        Console.WriteLine($"observations: {total} total, newest first");
        foreach (var observation in newest)
        {
            Console.WriteLine($"  {observation.Kind,-11} {observation.ObservedAt:O} {observation.Latitude:0.##},{observation.Longitude:0.##} " +
                              $"{observation.Summary ?? observation.AqiCategory ?? "-"} " +
                              $"{(observation.TemperatureC ?? observation.AqiValue)?.ToString("F1") ?? "-"}");
        }

        return 0;
    }

    private static async Task RunAsync(string name, Func<Task<string>> action)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var detail = await action();
            var elapsed = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
            Console.WriteLine($"[ok]   {name,-46} {elapsed,7:F0} ms  {detail}");
        }
        catch (Exception ex)
        {
            _failures++;
            var elapsed = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
            Console.WriteLine($"[FAIL] {name,-46} {elapsed,7:F0} ms  {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>Minimal stdout logger so the probe has no dependency on a logging package.</summary>
internal sealed class ProbeLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ProbeLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class ProbeLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Console.WriteLine($"  [{logLevel}] {category.Split('.').Last()}: {formatter(state, exception)}");
    }
}
