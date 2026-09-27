using Cortex.Mediator;
using Domain.Entities;
using Domain.Entities.Exceptions;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using ErrorOr;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;

namespace WebApi.Endpoints;

/// <summary>
/// Public weather surface. The forecast and AQI routes go through the mediator and the existing
/// provider selector, so they exercise the real registry → cache → provider chain; the remaining
/// routes expose the provider's extended datasets directly.
/// </summary>
public static class WeatherEndpoints
{
    public static IEndpointRouteBuilder MapWeatherEndpoints(this IEndpointRouteBuilder app)
    {
        var weather = app.MapGroup("/api/weather");

        weather.MapGet("/forecast", async (double lat, double lon, string? providerId, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            try
            {
                var result = await mediator.QueryAsync(new GetWeatherForecastQuery(location, providerId), ct);
                return result.IsError ? ToProblem(result.FirstError) : Results.Ok(result.Value);
            }
            catch (Exception ex) when (ex is AllProvidersUnavailableException or ProviderUnavailableException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "ProviderUnavailable");
            }
        })
        .WithName("WeatherForecastAtCoordinate")
        .WithSummary("Current conditions and forecast for a coordinate, served by the first healthy provider.");

        weather.MapGet("/aqi", async (double lat, double lon, string? providerId, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            try
            {
                var result = await mediator.QueryAsync(new GetAirQualityQuery(location, providerId), ct);
                return result.IsError ? ToProblem(result.FirstError) : Results.Ok(result.Value);
            }
            catch (Exception ex) when (ex is AllProvidersUnavailableException or ProviderUnavailableException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "ProviderUnavailable");
            }
        })
        .WithName("AirQualityAtCoordinate")
        .WithSummary("Current air quality (AQI, pollutants, pollen) for a coordinate.");

        weather.MapGet("/series", async (double lat, double lon, HttpContext http, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            var provider = http.RequestServices.GetService<IWeatherSeriesProvider>();
            if (provider is null)
            {
                return NotConfigured("weather time series");
            }

            try
            {
                return Results.Ok(await provider.GetForecastSeriesAsync(location, ct));
            }
            catch (Exception ex) when (ex is ProviderRequestException or ProviderUnavailableException or ProviderRateLimitedException)
            {
                return ToProviderProblem(ex);
            }
        })
        .WithName("WeatherSeriesAtCoordinate")
        .WithSummary("Full hourly/daily series for a coordinate when the serving provider exposes one.");

        weather.MapGet("/archive", async (double lat, double lon, DateOnly start, DateOnly end, HttpContext http, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            var provider = http.RequestServices.GetService<IHistoricalWeatherProvider>();
            if (provider is null)
            {
                return NotConfigured("historical archive");
            }

            try
            {
                return Results.Ok(await provider.GetArchiveAsync(location, start, end, ct));
            }
            catch (Exception ex) when (ex is ProviderRequestException or ProviderUnavailableException or ProviderRateLimitedException)
            {
                return ToProviderProblem(ex);
            }
        })
        .WithName("HistoricalArchiveAtCoordinate")
        .WithSummary("Reanalysis archive for a coordinate and inclusive date range.");

        weather.MapGet("/marine", async (double lat, double lon, HttpContext http, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            var provider = http.RequestServices.GetService<IMarineWeatherProvider>();
            if (provider is null)
            {
                return NotConfigured("marine weather");
            }

            try
            {
                return Results.Ok(await provider.GetMarineAsync(location, ct));
            }
            catch (Exception ex) when (ex is ProviderRequestException or ProviderUnavailableException or ProviderRateLimitedException)
            {
                return ToProviderProblem(ex);
            }
        })
        .WithName("MarineWeatherAtCoordinate")
        .WithSummary("Wave, swell and sea-surface data for a coastal coordinate.");

        weather.MapGet("/elevation", async (double lat, double lon, HttpContext http, CancellationToken ct) =>
        {
            if (!TryCoordinates(lat, lon, out var location, out var invalid))
            {
                return invalid;
            }

            var provider = http.RequestServices.GetService<IElevationProvider>();
            if (provider is null)
            {
                return NotConfigured("elevation");
            }

            try
            {
                var elevations = await provider.GetElevationsAsync([location], ct);
                return Results.Ok(new { location.Latitude, location.Longitude, ElevationMeters = elevations.FirstOrDefault() });
            }
            catch (Exception ex) when (ex is ProviderRequestException or ProviderUnavailableException or ProviderRateLimitedException)
            {
                return ToProviderProblem(ex);
            }
        })
        .WithName("ElevationAtCoordinate")
        .WithSummary("Ground elevation for a coordinate.");

        app.MapGet("/api/geocoding/search", async (string name, int? count, string? language, HttpContext http, CancellationToken ct) =>
        {
            var provider = http.RequestServices.GetService<IGeocodingProvider>();
            if (provider is null)
            {
                return NotConfigured("geocoding");
            }

            try
            {
                var places = await provider.SearchAsync(name, count ?? 5, language, ct);
                return Results.Ok(places.Select(p => new GeocodingResult(
                    p.Id, p.Name, p.Location.Latitude, p.Location.Longitude, p.ElevationMeters,
                    p.CountryCode, p.Country, p.Admin1, p.Timezone, p.Population)));
            }
            catch (Exception ex) when (ex is ProviderRequestException or ProviderUnavailableException or ProviderRateLimitedException)
            {
                return ToProviderProblem(ex);
            }
        })
        .WithName("PlaceSearchByName")
        .WithSummary("Place-name lookup used to turn a city name into coordinates.");

        return app;
    }

    private static bool TryCoordinates(double lat, double lon, out Coordinates location, out IResult invalid)
    {
        try
        {
            location = Coordinates.FromDegrees(lat, lon);
            invalid = Results.Empty;
            return true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            location = default;
            invalid = Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "InvalidCoordinates");
            return false;
        }
    }

    private static IResult ToProviderProblem(Exception ex) => ex switch
    {
        ProviderRateLimitedException rateLimited => Results.Problem(
            rateLimited.Message, statusCode: StatusCodes.Status429TooManyRequests, title: "ProviderRateLimited",
            extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = (rateLimited.RetryAfter ?? TimeSpan.Zero).TotalSeconds }),
        ProviderRequestException rejected => Results.Problem(
            rejected.Reason, statusCode: StatusCodes.Status400BadRequest, title: "ProviderRejectedRequest"),
        ProviderAuthenticationException auth => Results.Problem(
            auth.Message, statusCode: StatusCodes.Status502BadGateway, title: "ProviderAuthenticationFailed"),
        _ => Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "ProviderUnavailable")
    };

    private static IResult NotConfigured(string capability) => Results.Problem(
        $"No provider is registered for the '{capability}' capability.",
        statusCode: StatusCodes.Status501NotImplemented,
        title: "CapabilityNotConfigured");

    private static IResult ToProblem(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.Problem(error.Description, statusCode: StatusCodes.Status404NotFound, title: error.Code),
        ErrorType.Validation => Results.Problem(error.Description, statusCode: StatusCodes.Status400BadRequest, title: error.Code),
        ErrorType.Unauthorized => Results.Problem(error.Description, statusCode: StatusCodes.Status401Unauthorized, title: error.Code),
        ErrorType.Forbidden => Results.Problem(error.Description, statusCode: StatusCodes.Status403Forbidden, title: error.Code),
        _ => Results.Problem(error.Description, statusCode: StatusCodes.Status503ServiceUnavailable, title: error.Code)
    };
}

/// <summary>Compact geocoding projection so the API does not leak the domain record verbatim.</summary>
public sealed record GeocodingResult(
    long Id,
    string Name,
    double Latitude,
    double Longitude,
    double? ElevationMeters,
    string? CountryCode,
    string? Country,
    string? Admin1,
    string? Timezone,
    long? Population);
