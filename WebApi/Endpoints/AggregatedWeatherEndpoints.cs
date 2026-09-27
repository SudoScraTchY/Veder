using Domain.Caching;
using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.Enumerations;
using Domain.Entities.Exceptions;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Microsoft.Extensions.Options;
using Shared.Contracts.Responses;
// Qualified explicitly below: WebApi.WeatherForecast (the stock template class) shadows the domain record here.

// The stock ASP.NET template ships WebApi.WeatherForecast, which shadows the domain record in this
// namespace; alias the domain type so the intent here is unambiguous.

// The stock ASP.NET template ships WebApi.WeatherForecast, which shadows the domain record in this
// namespace; alias the domain type so the intent is unambiguous.

namespace WebApi.Endpoints;

/// <summary>
/// The one consolidated endpoint the web app reads. It puts the whole chain on the live path:
/// proximity-bucketed, short-TTL cache; availability-only failover that never caches a substitute
/// under the requested provider's key; and a provenance envelope so the UI can be honest about
/// degraded or substituted data.
/// </summary>
public static class AggregatedWeatherEndpoints
{
    public static IEndpointRouteBuilder MapAggregatedWeatherEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/weather/aggregate", async (
            double? lat,
            double? lon,
            string? name,
            string? providerId,
            bool? strictProvider,
            bool? includeAirQuality,
            HttpContext http,
            CancellationToken ct) =>
        {
            var services = http.RequestServices;
            var geocoding = services.GetService<IGeocodingProvider>();
            var weatherProviders = services.GetServices<IWeatherProvider>().ToList();
            var seriesProvider = services.GetService<IWeatherSeriesProvider>();
            var airQualityProviders = services.GetServices<IAirQualityProvider>().ToList();
            var registry = services.GetRequiredService<IProviderRegistry>();
            var cache = services.GetRequiredService<IWeatherCacheStore>();
            var options = services.GetRequiredService<IOptions<AggregationOptions>>().Value;

            // 1. Resolve the location: a place name takes precedence, otherwise the coordinates stand.
            ResolvedLocation location;

            if (!string.IsNullOrWhiteSpace(name))
            {
                if (geocoding is null)
                {
                    return Results.Problem("No geocoding provider is registered.", statusCode: StatusCodes.Status501NotImplemented, title: "CapabilityNotConfigured");
                }

                var matches = await geocoding.SearchAsync(name!, 1, null, ct);

                if (matches.Count == 0)
                {
                    return Results.Problem($"No location matched '{name}'.", statusCode: StatusCodes.Status404NotFound, title: "LocationNotFound");
                }

                var match = matches[0];
                location = new ResolvedLocation(match.Name, match.Location.Latitude, match.Location.Longitude, match.CountryCode, match.Country, match.Admin1, match.Timezone, 0);
            }
            else
            {
                if (lat is null || lon is null)
                {
                    return Results.Problem("Provide either a place name or both lat and lon.", statusCode: StatusCodes.Status400BadRequest, title: "InvalidCoordinates");
                }

                Coordinates coordinates;
                try
                {
                    coordinates = Coordinates.FromDegrees(lat.Value, lon.Value);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "InvalidCoordinates");
                }

                location = new ResolvedLocation(null, coordinates.Latitude, coordinates.Longitude, null, null, null, null, 0);
            }

            var point = Coordinates.FromDegrees(location.Latitude, location.Longitude);
            var bucket = GeoBucketer.For(point.Latitude, point.Longitude, options.GeoCellDegrees);
            var strict = strictProvider ?? false;

            // 2. Decide the order we are willing to ask, honouring the "provider is a preference" rule.
            var healthy = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);
            var order = BuildCandidateOrder(providerId, strict, healthy, weatherProviders.Select(p => p.ProviderId));

            if (order.Count == 0)
            {
                return Results.Problem("No provider is available for current conditions.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "ProviderUnavailable");
            }

            // 3. Walk the order, substituting only where the failure policy permits it.
            CacheReadResult<Domain.Entities.WeatherForecast>? read = null;
            var failures = new List<string>();

            foreach (var candidateId in order)
            {
                var provider = weatherProviders.FirstOrDefault(p => string.Equals(p.ProviderId, candidateId, StringComparison.OrdinalIgnoreCase));

                if (provider is null)
                {
                    continue;
                }

                var key = WeatherCacheKey.Compose(new WeatherCacheKeyParts
                {
                    DataType = WeatherDataType.CurrentConditions,
                    ProviderId = candidateId,
                    Bucket = bucket,
                    Units = options.Units,
                    Variables = options.CurrentVariables,
                    Timezone = options.Timezone
                });

                try
                {
                    read = await cache.GetOrCreateAsync(
                        key,
                        async token => new CacheProduced<Domain.Entities.WeatherForecast>(await provider.GetForecastAsync(point, token), candidateId),
                        CacheTtlPolicy.For(WeatherDataType.CurrentConditions),
                        null,
                        ct);

                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var kind = ProviderFailurePolicy.Classify(ex);
                    var outcome = ProviderFailurePolicy.ToOutcomeName(kind);
                    failures.Add($"{candidateId}: {outcome}");

                    if (ProviderFailurePolicy.AffectsProviderHealth(kind))
                    {
                        registry.MarkUnhealthy(candidateId);
                    }

                    // A substitute is only allowed for availability failures, and never for a strict caller.
                    if (!ProviderFailurePolicy.MaySubstitute(providerId, strict, kind))
                    {
                        return Results.Problem(
                            $"Provider '{candidateId}' failed with {outcome}.",
                            statusCode: kind == ProviderFailureKind.RateLimit ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable,
                            title: "ProviderUnavailable");
                    }
                }
            }

            if (read is null)
            {
                return Results.Problem($"No provider could serve current conditions ({string.Join("; ", failures)}).", statusCode: StatusCodes.Status503ServiceUnavailable, title: "AllProvidersUnavailable");
            }

            // 4. Daily outlook from the provider that actually served, and air quality separately so a
            //    failure there degrades one section instead of the whole payload.
            IReadOnlyList<DailyForecastItem> daily = [];

            if (seriesProvider is not null && string.Equals(seriesProvider.ProviderId, read.SourceProviderId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var series = await seriesProvider.GetForecastSeriesAsync(point, ct);
                    daily = BuildDaily(series);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failures.Add($"forecast: {ProviderFailurePolicy.ToOutcomeName(ProviderFailurePolicy.Classify(ex))}");
                }
            }

            AirQualityReading? air = null;

            if (includeAirQuality ?? true)
            {
                var airProvider = airQualityProviders.FirstOrDefault();
                if (airProvider is not null)
                {
                    try
                    {
                        air = await airProvider.GetCurrentAsync(point, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        failures.Add($"air-quality: {ProviderFailurePolicy.ToOutcomeName(ProviderFailurePolicy.Classify(ex))}");
                    }
                }
            }

            var provenance = AggregatedWeatherComposer.ProvenanceFrom(providerId, read, read.Outcome == CacheOutcome.Miss ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow - read.Age);

            var payload = AggregatedWeatherComposer.Compose(location, provenance, read.Value, daily, air, failures, includeAirQuality ?? true);

            return Results.Ok(payload);
        })
        .WithName("AggregatedWeather")
        .WithSummary("One consolidated payload: location, current conditions, daily outlook, air quality and provenance.");

        return app;
    }

    /// <summary>
    /// Preference first, then the healthy providers in priority order. A strict caller gets exactly one
    /// candidate so the failure surfaces instead of being hidden behind a substitute.
    /// </summary>
    private static List<string> BuildCandidateOrder(
        string? requestedProviderId,
        bool strict,
        IReadOnlyList<ProviderProfile> healthy,
        IEnumerable<string> registeredProviderIds)
    {
        var registered = registeredProviderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPriority = healthy.Select(p => p.Id).Where(registered.Contains).ToList();

        if (string.IsNullOrWhiteSpace(requestedProviderId))
        {
            return byPriority;
        }

        var order = new List<string> { requestedProviderId! };

        if (!strict)
        {
            order.AddRange(byPriority.Where(id => !string.Equals(id, requestedProviderId, StringComparison.OrdinalIgnoreCase)));
        }

        return order;
    }

    /// <summary>Groups series points into daily rows, skipping days the provider did not populate.</summary>
    private static List<DailyForecastItem> BuildDaily(WeatherSeries series)
    {
        var daily = new List<DailyForecastItem>();

        foreach (var day in series.Points.GroupBy(p => DateOnly.FromDateTime(p.Time.UtcDateTime)).OrderBy(g => g.Key))
        {
            var maximum = day.SelectMany(p => p.Values).Where(v => v.Key == "temperature_2m_max").Select(v => v.Value).DefaultIfEmpty(double.NaN).Max();
            var minimum = day.SelectMany(p => p.Values).Where(v => v.Key == "temperature_2m_min").Select(v => v.Value).DefaultIfEmpty(double.NaN).Min();

            if (double.IsNaN(maximum) || double.IsNaN(minimum))
            {
                continue;
            }

            var wind = day.SelectMany(p => p.Values).Where(v => v.Key.StartsWith("wind_speed", StringComparison.Ordinal)).Select(v => v.Value).DefaultIfEmpty(0).Max();
            var precipitation = day.SelectMany(p => p.Values).Where(v => v.Key == "precipitation_sum").Select(v => v.Value).DefaultIfEmpty(0).Max();

            daily.Add(new DailyForecastItem(
                Date: day.Key,
                TemperatureMaxC: Math.Round(maximum, 1),
                TemperatureMinC: Math.Round(minimum, 1),
                Summary: string.Empty,
                PrecipitationMm: Math.Round(precipitation, 1),
                WindSpeedMaxKph: Math.Round(wind, 1)));
        }

        return daily;
    }
}

/// <summary>Knobs the aggregated endpoint shares with the cache, bound from the "Aggregation" section.</summary>
public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    /// <summary>Proximity cell used to share cache entries between nearby requests.</summary>
    public double GeoCellDegrees { get; set; } = GeoBucketer.DefaultCellDegrees;

    public string Units { get; set; } = "metric";

    public string Timezone { get; set; } = "auto";

    public IReadOnlyList<string> CurrentVariables { get; set; } =
    [
        "temperature_2m", "relative_humidity_2m", "apparent_temperature", "precipitation",
        "weather_code", "cloud_cover", "pressure_msl", "wind_speed_10m", "wind_direction_10m", "wind_gusts_10m"
    ];
}
