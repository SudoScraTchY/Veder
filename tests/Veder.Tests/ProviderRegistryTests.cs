using Domain.Entities.Enumerations;
using Infrastructure.Providers;

namespace Veder.Tests;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void DefaultSeeds_AreSevenProvidersOrderedByPriority()
    {
        var registry = new ProviderRegistry();

        var all = registry.GetAll();

        Assert.Equal(7, all.Count);
        Assert.Equal(
            ["open-meteo", "openweathermap", "weatherapi", "nws", "met.no", "openaq", "waqi"],
            all.Select(p => p.Id));
        Assert.Equal(Enumerable.Range(1, 7), all.Select(p => p.Priority));
    }

    [Fact]
    public void HealthyByPriority_ExcludesDisabledProviders()
    {
        var registry = new ProviderRegistry();
        registry.SetEnabledAsync("open-meteo", false, CancellationToken.None).GetAwaiter().GetResult();

        var healthy = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);

        Assert.DoesNotContain(healthy, p => p.Id == "open-meteo");
        Assert.Equal("openweathermap", healthy[0].Id);
    }

    [Fact]
    public void HealthyByPriority_ExcludesUnhealthyProviders()
    {
        var registry = new ProviderRegistry();
        registry.MarkUnhealthy("open-meteo");

        var healthy = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);

        Assert.DoesNotContain(healthy, p => p.Id == "open-meteo");
        Assert.False(registry.Get("open-meteo")!.IsHealthy);
    }

    [Fact]
    public void HealthyByPriority_ExcludesQuotaExhaustedProviders()
    {
        var registry = new ProviderRegistry([
            new ProviderSeed("exhausted", "Exhausted", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 1, DailyQuota: 3, UsedQuota: 3),
            new ProviderSeed("available", "Available", ProviderCapability.CurrentWeather | ProviderCapability.Forecast, 2, DailyQuota: 3, UsedQuota: 2)
        ]);

        var healthy = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);

        Assert.Single(healthy);
        Assert.Equal("available", healthy[0].Id);
    }

    [Fact]
    public void HealthyByPriority_FollowsUpdatedPriority()
    {
        var registry = new ProviderRegistry();
        registry.SetPriorityAsync("met.no", 0, CancellationToken.None).GetAwaiter().GetResult();

        var healthy = registry.GetHealthyByPriority(ProviderCapability.CurrentWeather | ProviderCapability.Forecast);

        Assert.Equal("met.no", healthy[0].Id);
    }

    [Fact]
    public void Mutation_OnUnknownProviderId_ReportsFalse()
    {
        var registry = new ProviderRegistry();

        Assert.False(registry.SetEnabledAsync("does-not-exist", false, CancellationToken.None).GetAwaiter().GetResult());
        Assert.False(registry.SetPriorityAsync("does-not-exist", 1, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(registry.Get("does-not-exist"));
    }

    [Fact]
    public void RecordUsage_IncrementsTheQuotaCounter()
    {
        var registry = new ProviderRegistry([
            new ProviderSeed("metered", "Metered", ProviderCapability.CurrentWeather, 1, DailyQuota: 2)
        ]);

        registry.RecordUsage("metered");

        Assert.Equal(1, registry.Get("metered")!.UsedQuota);
        Assert.True(registry.Get("metered")!.HasQuotaRemaining);

        registry.RecordUsage("metered");
        registry.RecordUsage("metered");

        var metered = registry.Get("metered")!;
        Assert.Equal(3, metered.UsedQuota);
        Assert.False(metered.HasQuotaRemaining);
        Assert.Empty(registry.GetHealthyByPriority(ProviderCapability.CurrentWeather));
    }

    [Fact]
    public void HealthTransitions_AreVisibleThroughGet()
    {
        var registry = new ProviderRegistry();

        registry.MarkUnhealthy("nws");
        Assert.False(registry.Get("nws")!.IsHealthy);

        registry.MarkHealthy("nws");
        Assert.True(registry.Get("nws")!.IsHealthy);
    }
}
