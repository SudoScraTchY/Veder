using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;

namespace Infrastructure.Cache;

public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    public string ForWeather(string providerId, Coordinates location) => $"weather:{providerId}:{location.ToCacheBucket()}";
    public string ForAirQuality(string providerId, Coordinates location) => $"aqi:{providerId}:{location.ToCacheBucket()}";
}
