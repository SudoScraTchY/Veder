using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// A declarative description of one Open-Meteo read: which dataset, where, which groupings and
/// variables, and the optional date range / model override that the dataset supports.
/// </summary>
public sealed record OpenMeteoRequest
{
    public required OpenMeteoDataset Dataset { get; init; }

    public required Coordinates Location { get; init; }

    /// <summary>Wire grouping name ("current", "minutely_15", "hourly", "daily") to the requested variables.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Groups { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>Overrides <see cref="OpenMeteoOptions.Models"/> for this call.</summary>
    public string? Models { get; init; }

    /// <summary>Overrides <see cref="OpenMeteoOptions.Timezone"/> for this call.</summary>
    public string? Timezone { get; init; }

    /// <summary>Overrides <see cref="OpenMeteoOptions.CellSelection"/> for this call.</summary>
    public string? CellSelection { get; init; }

    /// <summary>Overrides <see cref="OpenMeteoOptions.ForecastDays"/> for forecast-shaped datasets.</summary>
    public int? ForecastDays { get; init; }

    /// <summary>Overrides <see cref="OpenMeteoOptions.PastDays"/> for forecast-shaped datasets.</summary>
    public int? PastDays { get; init; }
}

/// <summary>A request that has been validated and rendered into a concrete URL.</summary>
public sealed record OpenMeteoBuiltRequest(
    OpenMeteoDataset Dataset,
    string Operation,
    string Url,
    /// <summary>The same URL with the API key masked, safe for logs and the record store.</summary>
    string SanitizedUrl,
    IReadOnlyList<string> Variables,
    IReadOnlyList<string> Groups);

/// <summary>Validates Open-Meteo requests against the documented variable catalogue and dataset rules, then renders them.</summary>
public static class OpenMeteoRequestBuilder
{
    /// <summary>Open-Meteo's reanalysis archive begins here.</summary>
    public static readonly DateOnly ArchiveEarliest = new(1940, 1, 1);

    /// <summary>Climate projections are published for 1950–2050.</summary>
    public static readonly DateOnly ClimateEarliest = new(1950, 1, 1);

    public static readonly DateOnly ClimateLatest = new(2050, 12, 31);

    /// <summary>The archive lags real time, so requests closer than this to today are rejected up front.</summary>
    public const int ArchiveLagDays = 5;

    public static OpenMeteoBuiltRequest Build(OpenMeteoRequest request, OpenMeteoOptions options, DateOnly today)
    {
        var problems = new List<string>();

        if (request.Groups.Count == 0)
        {
            problems.Add("at least one grouping must be requested");
        }

        var acceptedGroups = OpenMeteoVariables.Groups(request.Dataset);

        foreach (var (group, variables) in request.Groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!acceptedGroups.Contains(group, StringComparer.Ordinal))
            {
                problems.Add($"dataset '{request.Dataset}' does not accept the '{group}' grouping (accepted: {string.Join(", ", acceptedGroups)})");
                continue;
            }

            if (variables.Count == 0)
            {
                problems.Add($"grouping '{group}' was requested with no variables");
                continue;
            }

            var unknown = variables.Where(v => !OpenMeteoVariables.IsKnown(request.Dataset, group, v)).ToArray();
            if (unknown.Length > 0)
            {
                problems.Add($"unknown variable(s) for {request.Dataset}/{group}: {string.Join(", ", unknown)}");
            }
        }

        var isDated = request.Dataset is OpenMeteoDataset.Archive or OpenMeteoDataset.HistoricalForecast or OpenMeteoDataset.Climate;
        var lowerBound = request.Dataset switch
        {
            OpenMeteoDataset.Archive => ArchiveEarliest,
            OpenMeteoDataset.Climate => ClimateEarliest,
            _ => DateOnly.MinValue
        };
        var upperBound = request.Dataset switch
        {
            OpenMeteoDataset.Archive => today.AddDays(-ArchiveLagDays),
            OpenMeteoDataset.Climate => ClimateLatest,
            _ => today
        };

        if (isDated)
        {
            if (request.StartDate is null || request.EndDate is null)
            {
                problems.Add($"dataset '{request.Dataset}' requires both StartDate and EndDate");
            }
            else if (request.StartDate > request.EndDate)
            {
                problems.Add($"StartDate {request.StartDate:yyyy-MM-dd} is after EndDate {request.EndDate:yyyy-MM-dd}");
            }
            else
            {
                if (request.StartDate < lowerBound) problems.Add($"StartDate {request.StartDate:yyyy-MM-dd} precedes the earliest available date {lowerBound:yyyy-MM-dd}");
                if (request.EndDate > upperBound) problems.Add($"EndDate {request.EndDate:yyyy-MM-dd} is beyond the latest available date {upperBound:yyyy-MM-dd}");
            }
        }
        else if (request.StartDate is not null || request.EndDate is not null)
        {
            problems.Add($"dataset '{request.Dataset}' is forecast-shaped and does not accept a date range");
        }

        if (problems.Count > 0)
        {
            throw new Domain.Entities.Exceptions.ProviderRequestException(
                OpenMeteoProviderConstants.ProviderId,
                string.Join("; ", problems));
        }

        var forecastDays = request.ForecastDays ?? options.ForecastDays;
        var pastDays = request.PastDays ?? options.PastDays;
        var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? options.Timezone : request.Timezone!;
        var cellSelection = string.IsNullOrWhiteSpace(request.CellSelection) ? options.CellSelection : request.CellSelection!;
        var models = string.IsNullOrWhiteSpace(request.Models) ? options.Models : request.Models;

        var query = new List<string>
        {
            $"latitude={request.Location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"longitude={request.Location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "timeformat=unixtime",
            $"timezone={Uri.EscapeDataString(timezone)}"
        };

        foreach (var (group, variables) in request.Groups.OrderBy(g => GroupOrder(g.Key)))
        {
            query.Add($"{group}={Uri.EscapeDataString(string.Join(",", variables))}");
        }

        switch (request.Dataset)
        {
            case OpenMeteoDataset.Forecast:
                query.Add($"forecast_days={forecastDays}");
                if (pastDays > 0) query.Add($"past_days={pastDays}");
                query.Add($"cell_selection={Uri.EscapeDataString(cellSelection)}");
                query.Add($"temperature_unit={options.TemperatureUnit}");
                query.Add($"wind_speed_unit={options.WindSpeedUnit}");
                query.Add($"precipitation_unit={options.PrecipitationUnit}");
                if (!string.IsNullOrWhiteSpace(models)) query.Add($"models={Uri.EscapeDataString(models!)}");
                break;
            case OpenMeteoDataset.Marine:
                query.Add($"forecast_days={forecastDays}");
                query.Add($"cell_selection={Uri.EscapeDataString(cellSelection)}");
                break;
            case OpenMeteoDataset.Ensemble:
            case OpenMeteoDataset.Flood:
                query.Add($"forecast_days={forecastDays}");
                if (!string.IsNullOrWhiteSpace(models)) query.Add($"models={Uri.EscapeDataString(models!)}");
                break;
            case OpenMeteoDataset.Archive:
            case OpenMeteoDataset.HistoricalForecast:
                query.Add($"start_date={request.StartDate:yyyy-MM-dd}");
                query.Add($"end_date={request.EndDate:yyyy-MM-dd}");
                break;
            case OpenMeteoDataset.Climate:
                query.Add($"start_date={request.StartDate:yyyy-MM-dd}");
                query.Add($"end_date={request.EndDate:yyyy-MM-dd}");
                if (!string.IsNullOrWhiteSpace(models)) query.Add($"models={Uri.EscapeDataString(models!)}");
                break;
            case OpenMeteoDataset.AirQuality:
                query.Add($"forecast_days={forecastDays}");
                if (pastDays > 0) query.Add($"past_days={pastDays}");
                break;
        }

        var baseUrl = request.Dataset switch
        {
            OpenMeteoDataset.Forecast => OpenMeteoEndpoints.Forecast,
            OpenMeteoDataset.Archive => OpenMeteoEndpoints.Archive,
            OpenMeteoDataset.HistoricalForecast => OpenMeteoEndpoints.HistoricalForecast,
            OpenMeteoDataset.AirQuality => OpenMeteoEndpoints.AirQuality,
            OpenMeteoDataset.Marine => OpenMeteoEndpoints.Marine,
            OpenMeteoDataset.Ensemble => OpenMeteoEndpoints.Ensemble,
            OpenMeteoDataset.Climate => OpenMeteoEndpoints.Climate,
            OpenMeteoDataset.Flood => OpenMeteoEndpoints.Flood,
            _ => OpenMeteoEndpoints.Forecast
        };

        var url = $"{baseUrl}?{string.Join("&", query)}";
        var sanitized = options.HasApiKey ? $"{url}&apikey=***" : url;
        var live = options.HasApiKey ? $"{url}&apikey={Uri.EscapeDataString(options.ApiKey!)}" : url;

        return new OpenMeteoBuiltRequest(
            Dataset: request.Dataset,
            Operation: OperationName(request.Dataset),
            Url: live,
            SanitizedUrl: sanitized,
            Variables: request.Groups.SelectMany(g => g.Value).Distinct(StringComparer.Ordinal).ToList(),
            Groups: request.Groups.Keys.ToList());
    }

    /// <summary>Stable dataset name used in records, metrics and logs.</summary>
    public static string OperationName(OpenMeteoDataset dataset) => dataset switch
    {
        OpenMeteoDataset.Forecast => "forecast",
        OpenMeteoDataset.Archive => "archive",
        OpenMeteoDataset.HistoricalForecast => "historical-forecast",
        OpenMeteoDataset.AirQuality => "air-quality",
        OpenMeteoDataset.Marine => "marine",
        OpenMeteoDataset.Ensemble => "ensemble",
        OpenMeteoDataset.Climate => "climate",
        OpenMeteoDataset.Flood => "flood",
        _ => dataset.ToString().ToLowerInvariant()
    };

    /// <summary>Open-Meteo expects current before the series groupings; ordering keeps URLs stable for caching and diffing.</summary>
    private static int GroupOrder(string group) => group switch
    {
        "current" => 0,
        "minutely_15" => 1,
        "hourly" => 2,
        "daily" => 3,
        _ => 4
    };
}
