using Domain.Caching;
using Domain.Entities.Interfaces;
using Infrastructure.Caching;
using Infrastructure.Cache;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Helpers.DI;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Cache: ICacheService sits on top of whatever IDistributedCache the host registered
        // (Redis/Garnet in production via Aspire, in-memory for standalone runs and tests).
        services.AddSingleton<ICacheService, DistributedCacheService>();
        services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();

        // Read-through cache: proximity-bucketed keys, per-type TTL, single-flight refresh.
        services.AddSingleton<IWeatherCacheStore, InMemoryWeatherCacheStore>();

        // Provider state is process-wide: it carries health, priority and quota counters.
        // Registered through an explicit factory: the type also exposes a seeding constructor
        // taking IEnumerable<ProviderSeed>, which the container would otherwise satisfy with an
        // empty sequence and silently boot a provider-less registry.
        services.AddSingleton<IProviderRegistry>(_ => new ProviderRegistry());
        services.AddScoped<IProviderSelector, ProviderSelector>();

        // Saved locations: process-local until the durable MongoDB repository (plan Phase 4) lands.
        services.AddSingleton<ISavedLocationRepository, InMemorySavedLocationRepository>();

        return services;
    }
}
