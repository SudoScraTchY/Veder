using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// The remaining documented Open-Meteo datasets — historical archive, historical forecast, marine,
/// ensemble, climate projection, flood and elevation, plus geocoding — behind the domain's
/// capability interfaces. One class keeps the wire plumbing in a single, auditable place; each
/// method is a thin, fully implemented mapping onto <see cref="WeatherSeries"/> or its own type.
/// </summary>
public sealed class OpenMeteoExtendedProvider(OpenMeteoClient client) :
    IHistoricalWeatherProvider,
    IMarineWeatherProvider,
    IEnsembleWeatherProvider,
    IClimateProjectionProvider,
    IFloodForecastProvider,
    IElevationProvider,
    IGeocodingProvider
{
    private static readonly IReadOnlyList<string> ArchiveDaily =
        ["temperature_2m_max", "temperature_2m_min", "precipitation_sum", "weather_code", "wind_speed_10m_max"];
    private static readonly IReadOnlyList<string> ArchiveHourly =
        ["temperature_2m", "relative_humidity_2m", "precipitation", "wind_speed_10m"];
    private static readonly IReadOnlyList<string> MarineRequested =
        ["wave_height", "wave_direction", "wave_period", "swell_wave_height", "sea_surface_temperature", "ocean_current_velocity"];
    private static readonly IReadOnlyList<string> EnsembleRequested =
        ["temperature_2m", "precipitation", "wind_speed_10m", "wind_gusts_10m"];
    private static readonly IReadOnlyList<string> ClimateRequested =
        ["temperature_2m_max", "temperature_2m_min", "temperature_2m_mean", "precipitation_sum", "relative_humidity_2m_mean"];
    private static readonly IReadOnlyList<string> FloodRequested =
        ["river_discharge", "river_discharge_max", "river_discharge_p75"];

    public string ProviderId => OpenMeteoProviderConstants.ProviderId;

    public Task<WeatherSeries> GetArchiveAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct) =>
        GetSeriesAsync(OpenMeteoDataset.Archive, location, startDate, endDate, ct);

    public Task<WeatherSeries> GetHistoricalForecastAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct) =>
        GetSeriesAsync(OpenMeteoDataset.HistoricalForecast, location, startDate, endDate, ct);

    public Task<WeatherSeries> GetMarineAsync(Coordinates location, CancellationToken ct) =>
        GetForecastShapedAsync(OpenMeteoDataset.Marine, location, ["current", "hourly"], MarineRequested, ct);

    public Task<WeatherSeries> GetEnsembleAsync(Coordinates location, CancellationToken ct) =>
        GetForecastShapedAsync(OpenMeteoDataset.Ensemble, location, ["hourly"], EnsembleRequested, ct);

    public Task<WeatherSeries> GetClimateProjectionAsync(Coordinates location, DateOnly startDate, DateOnly endDate, CancellationToken ct) =>
        GetSeriesAsync(OpenMeteoDataset.Climate, location, startDate, endDate, ct);

    public Task<WeatherSeries> GetFloodAsync(Coordinates location, CancellationToken ct) =>
        GetForecastShapedAsync(OpenMeteoDataset.Flood, location, ["daily"], FloodRequested, ct);

    public async Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<Coordinates> locations, CancellationToken ct) =>
        await client.GetElevationsAsync(locations, ct);

    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(string name, int count, string? language, CancellationToken ct) =>
        await client.SearchLocationsAsync(name, count, language, ct);

    public async Task<GeoLocation?> GetByIdAsync(long id, CancellationToken ct) =>
        (await client.GetLocationsByIdAsync([id], ct)).FirstOrDefault();

    public async Task<IReadOnlyList<GeoLocation>> GetByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct) =>
        await client.GetLocationsByIdAsync(ids, ct);

    private async Task<WeatherSeries> GetSeriesAsync(
        OpenMeteoDataset dataset,
        Coordinates location,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var request = new OpenMeteoRequest
        {
            Dataset = dataset,
            Location = location,
            StartDate = startDate,
            EndDate = endDate,
            Groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["daily"] = dataset == OpenMeteoDataset.Climate ? ClimateRequested : ArchiveDaily,
                ["hourly"] = dataset == OpenMeteoDataset.Climate ? [] : ArchiveHourly
            }
                .Where(pair => pair.Value.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };

        var envelope = await client.GetSeriesAsync(request, ct);
        return OpenMeteoMapper.ToSeries(envelope);
    }

    private async Task<WeatherSeries> GetForecastShapedAsync(
        OpenMeteoDataset dataset,
        Coordinates location,
        IReadOnlyList<string> groups,
        IReadOnlyList<string> variables,
        CancellationToken ct)
    {
        var request = new OpenMeteoRequest
        {
            Dataset = dataset,
            Location = location,
            Groups = groups.ToDictionary(group => group, _ => variables, StringComparer.Ordinal)
        };

        var envelope = await client.GetSeriesAsync(request, ct);
        return OpenMeteoMapper.ToSeries(envelope);
    }
}
