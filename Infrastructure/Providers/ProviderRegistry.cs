using System.Collections.Concurrent;
using Domain.Entities;
using Domain.Entities.Enumerations;
using Domain.Entities.Interfaces;

namespace Infrastructure.Providers;

public sealed record ProviderSeed(
    string Id,
    string DisplayName,
    ProviderCapability Capabilities,
    int Priority,
    bool IsEnabled = true,
    int? DailyQuota = null,
    int UsedQuota = 0,
    bool IsHealthy = true);

public sealed class ProviderRegistry : IProviderRegistry
{
    private sealed class Entry
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required ProviderCapability Capabilities { get; init; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public int? DailyQuota { get; init; }
        public int UsedQuota { get; set; }
        public bool IsHealthy { get; set; }

        public ProviderProfile ToProfile()
        {
            var profile = new ProviderProfile
            {
                Id = Id,
                DisplayName = DisplayName,
                Capabilities = Capabilities,
                Priority = Priority,
                IsEnabled = IsEnabled,
                DailyQuota = DailyQuota
            };

            for (var i = 0; i < UsedQuota; i++) profile.IncrementQuota();

            if (IsHealthy) profile.MarkHealthy();
            else profile.MarkUnhealthy();

            return profile;
        }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRegistry() : this(DefaultSeeds) { }

    public ProviderRegistry(IEnumerable<ProviderSeed> seeds)
    {
        foreach (var seed in seeds)
        {
            _entries[seed.Id] = new Entry
            {
                Id = seed.Id,
                DisplayName = seed.DisplayName,
                Capabilities = seed.Capabilities,
                Priority = seed.Priority,
                IsEnabled = seed.IsEnabled,
                DailyQuota = seed.DailyQuota,
                UsedQuota = seed.UsedQuota,
                IsHealthy = seed.IsHealthy
            };
        }
    }

    public static IReadOnlyList<ProviderSeed> DefaultSeeds { get; } =
    [
        new("open-meteo", "Open-Meteo", ProviderCapability.CurrentWeather | ProviderCapability.Forecast | ProviderCapability.AirQuality | ProviderCapability.Historical | ProviderCapability.Marine | ProviderCapability.Ensemble | ProviderCapability.Climate | ProviderCapability.Flood | ProviderCapability.Elevation | ProviderCapability.Geocoding, 1),
        new("openweathermap", "OpenWeatherMap", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 2),
        new("weatherapi", "WeatherAPI", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 3),
        new("nws", "National Weather Service", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 4),
        new("met.no", "MET Norway", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 5),
        new("openaq", "OpenAQ", ProviderCapability.AirQuality, 6),
        new("waqi", "World Air Quality Index", ProviderCapability.AirQuality, 7)
    ];

    public IReadOnlyList<ProviderProfile> GetAll()
        => _entries.Values.Select(e => e.ToProfile()).OrderBy(p => p.Priority).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

    public ProviderProfile? Get(string providerId)
        => _entries.TryGetValue(providerId, out var entry) ? entry.ToProfile() : null;

    public IReadOnlyList<ProviderProfile> GetHealthyByPriority(ProviderCapability capability)
        => _entries.Values
            .Where(e => e.IsEnabled && e.IsHealthy)
            .Where(e => !e.DailyQuota.HasValue || e.UsedQuota < e.DailyQuota.Value)
            .Where(e => capability != ProviderCapability.None && (e.Capabilities & capability) != ProviderCapability.None)
            .OrderBy(e => e.Priority).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => e.ToProfile())
            .ToList();

    public Task<bool> SetEnabledAsync(string providerId, bool enabled, CancellationToken ct)
    {
        if (_entries.TryGetValue(providerId, out var entry))
        {
            entry.IsEnabled = enabled;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> SetPriorityAsync(string providerId, int priority, CancellationToken ct)
    {
        if (_entries.TryGetValue(providerId, out var entry))
        {
            entry.Priority = priority;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public void MarkHealthy(string providerId)
    {
        if (_entries.TryGetValue(providerId, out var entry)) entry.IsHealthy = true;
    }

    public void MarkUnhealthy(string providerId)
    {
        if (_entries.TryGetValue(providerId, out var entry)) entry.IsHealthy = false;
    }

    public void RecordUsage(string providerId)
    {
        if (_entries.TryGetValue(providerId, out var entry)) entry.UsedQuota++;
    }
}
