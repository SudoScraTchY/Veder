using Domain.Entities.ValueObjects;
using Infrastructure.Cache;

namespace Veder.Tests;

public sealed class CacheKeyBuilderTests
{
    private static readonly CacheKeyBuilder Keys = new();

    [Fact]
    public void ForWeather_UsesProviderAndTwoDecimalBucket()
    {
        var key = Keys.ForWeather("open-meteo", Coordinates.FromDegrees(51.5074, -0.1278));

        Assert.Equal("weather:open-meteo:51.51:-0.13", key);
    }

    [Fact]
    public void ForAirQuality_UsesItsOwnNamespace()
    {
        var location = Coordinates.FromDegrees(51.5074, -0.1278);

        Assert.Equal("aqi:openaq:51.51:-0.13", Keys.ForAirQuality("openaq", location));
        Assert.NotEqual(Keys.ForWeather("openaq", location), Keys.ForAirQuality("openaq", location));
    }

    [Fact]
    public void NearbyCoordinates_CollapseIntoTheSameBucket()
    {
        var a = Keys.ForWeather("nws", Coordinates.FromDegrees(40.7128, -74.0060));
        var b = Keys.ForWeather("nws", Coordinates.FromDegrees(40.7131, -74.0061));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Keys_AreStableAcrossCalls()
    {
        var location = Coordinates.FromDegrees(-33.8688, 151.2093);

        Assert.Equal(Keys.ForWeather("weatherapi", location), Keys.ForWeather("weatherapi", location));
    }
}
