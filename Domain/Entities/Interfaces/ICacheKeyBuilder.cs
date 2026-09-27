using Domain.Entities.ValueObjects;

namespace Domain.Entities.Interfaces;

public interface ICacheKeyBuilder
{
    string ForWeather(string providerId, Coordinates location);
    string ForAirQuality(string providerId, Coordinates location);
}