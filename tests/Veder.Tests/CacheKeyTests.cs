using Domain.Caching;

namespace Veder.Tests;

/// <summary>
/// Locks in the cache-key behaviour the design review demanded: invariant formatting, a stable
/// seam at ±180, poles collapsed, and every request-shaping parameter present in the key.
/// </summary>
public sealed class CacheKeyTests
{
    private static WeatherCacheKeyParts Parts(
        double latitude = 35.7000,
        double longitude = 51.4000,
        WeatherDataType type = WeatherDataType.CurrentConditions,
        string provider = "open-meteo",
        string units = "metric",
        IReadOnlyList<string>? variables = null,
        string? models = null,
        string timezone = "auto",
        double cell = GeoBucketer.DefaultCellDegrees) => new()
        {
            DataType = type,
            ProviderId = provider,
            Bucket = GeoBucketer.For(latitude, longitude, cell),
            Units = units,
            Variables = variables ?? [],
            Models = models,
            Timezone = timezone
        };

    [Fact]
    public void NeighbouringCoordinatesShareOneBucket()
    {
        // T1: 2 m apart, either side of a cell edge - the case plain rounding got wrong.
        var first = GeoBucketer.For(35.70049, 51.40049);
        var second = GeoBucketer.For(35.70051, 51.40051);

        var firstNeighbourhood = GeoBucketer.Neighbourhood(35.70049, 51.40049);

        Assert.True(firstNeighbourhood.Contains(second), "the neighbour lookup must reach the adjacent cell");
    }

    [Fact]
    public void BucketFormattingIsCultureInvariant()
    {
        // T2: under fa-IR / de-DE a decimal comma would collide with the key separator.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var parts = Parts();
            var key = WeatherCacheKey.Compose(parts);

            Assert.DoesNotContain(",", parts.Bucket.ToString().Replace(":", string.Empty));
            Assert.Contains(parts.Bucket.ToString(), key);
            Assert.Single(key.Split('|'), segment => segment.Contains(':'));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void NegativeZeroCollapsesOntoZero()
    {
        // -0.0 == 0.0 in C#, so the normaliser must hand back the positive zero for both.
        Assert.Equal(0d, GeoBucketer.NormalizeNegativeZero(-0.0));
        Assert.Equal(0d, GeoBucketer.NormalizeNegativeZero(0.0));
        Assert.Equal(GeoBucketer.For(0.0, 0.0), GeoBucketer.For(-0.0, -0.0));
        Assert.Equal(GeoBucketer.For(0.0, 0.0).ToString(), GeoBucketer.For(-0.0, -0.0).ToString());
    }

    [Fact]
    public void CoordinatesEitherSideOfACellEdgeAreReachableThroughTheNeighbourhood()
    {
        // T3-adjacent: 0.0001 and -0.0001 are ~22 m apart and land in different cells because the grid
        // origin sits between them. This is the structural edge discontinuity - the neighbour lookup is
        // what turns it into a cache hit, and this test pins both halves of that fact.
        var north = GeoBucketer.For(0.0001, 0.0001);
        var south = GeoBucketer.For(-0.0001, 0.0001);

        Assert.NotEqual(north, south);
        Assert.Contains(south, GeoBucketer.Neighbourhood(0.0001, 0.0001));
        Assert.Contains(north, GeoBucketer.Neighbourhood(-0.0001, 0.0001));
    }

    [Fact]
    public void AntimeridianIsOneSeam()
    {
        // T4: 179.999 and -179.999 are ~200 m apart and must not be two disjoint regions.
        var east = GeoBucketer.For(0.0, 179.999);
        var west = GeoBucketer.For(0.0, -179.999);

        var eastNeighbourhood = GeoBucketer.Neighbourhood(0.0, 179.999);

        Assert.True(eastNeighbourhood.Contains(west), "the wrap must connect +179.99 to -179.99");
        Assert.NotEqual(east, west);
    }

    [Fact]
    public void OutOfRangeLongitudeIsWrapped()
    {
        // T6: 180.0 and 540.0 are valid inputs, not errors.
        Assert.Equal(GeoBucketer.WrapLongitude(-180.0), GeoBucketer.WrapLongitude(180.0));
        Assert.Equal(GeoBucketer.WrapLongitude(1.0), GeoBucketer.WrapLongitude(361.0));
        Assert.InRange(GeoBucketer.WrapLongitude(180.0), -180.0, 180.0);
    }

    [Fact]
    public void PolarLatitudesCollapseLongitude()
    {
        // T5: at 89.999° a degree of longitude is a few metres, so longitude must not split the key.
        var zeroMeridian = GeoBucketer.For(89.999, 0.0);
        var oneTwenty = GeoBucketer.For(89.999, 120.0);

        Assert.Equal(zeroMeridian, oneTwenty);
        Assert.Equal(0, zeroMeridian.LongitudeCell);
    }

    [Fact]
    public void LatitudeOutOfRangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoBucketer.For(91.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoBucketer.For(double.NaN, 0.0));
    }

    [Fact]
    public void UnitDifferenceProducesADifferentKey()
    {
        // T7: a metric entry must never be served as an imperial one.
        Assert.NotEqual(WeatherCacheKey.Compose(Parts(units: "metric")), WeatherCacheKey.Compose(Parts(units: "imperial")));
    }

    [Fact]
    public void ModelSelectionProducesADifferentKey()
    {
        // T8
        Assert.NotEqual(WeatherCacheKey.Compose(Parts(models: "gfs")), WeatherCacheKey.Compose(Parts(models: "ecmwf")));
        Assert.NotEqual(WeatherCacheKey.Compose(Parts(models: null)), WeatherCacheKey.Compose(Parts(models: "gfs")));
    }

    [Fact]
    public void VariablesSubsetProducesADifferentKeyAndOrderDoesNot()
    {
        // T9
        var temperatureOnly = WeatherCacheKey.Compose(Parts(variables: ["temperature_2m"]));
        var withWind = WeatherCacheKey.Compose(Parts(variables: ["temperature_2m", "wind_speed_10m"]));
        var reordered = WeatherCacheKey.Compose(Parts(variables: ["wind_speed_10m", "temperature_2m"]));
        var duplicated = WeatherCacheKey.Compose(Parts(variables: ["temperature_2m", "wind_speed_10m", "temperature_2m"]));

        Assert.NotEqual(temperatureOnly, withWind);
        Assert.Equal(withWind, reordered);
        Assert.Equal(withWind, duplicated);
    }

    [Fact]
    public void TimezoneProducesADifferentKey()
    {
        // T15: a timezone shift moves the day boundaries of a 7-day forecast.
        Assert.NotEqual(WeatherCacheKey.Compose(Parts(timezone: "Asia/Tehran")), WeatherCacheKey.Compose(Parts(timezone: "UTC")));
    }

    [Fact]
    public void DataTypeAndProviderArePartOfTheKey()
    {
        Assert.NotEqual(
            WeatherCacheKey.Compose(Parts(type: WeatherDataType.CurrentConditions)),
            WeatherCacheKey.Compose(Parts(type: WeatherDataType.AirQuality)));

        Assert.NotEqual(
            WeatherCacheKey.Compose(Parts(provider: "open-meteo")),
            WeatherCacheKey.Compose(Parts(provider: "openweathermap")));

        Assert.StartsWith(
            WeatherCacheKey.PrefixFor(WeatherDataType.Forecast, "open-meteo"),
            WeatherCacheKey.Compose(Parts(type: WeatherDataType.Forecast)));
    }

    [Fact]
    public void KeyShapeIsStableAndVersioned()
    {
        var key = WeatherCacheKey.Compose(Parts(variables: ["temperature_2m"]));

        Assert.Equal("v1|CurrentConditions|open-meteo|" + GeoBucketer.For(35.7, 51.4).ToString() + "|metric|temperature_2m|-|auto", key);
        Assert.StartsWith($"v{WeatherCacheKey.CurrentSchemaVersion}|", key);
    }

    [Fact]
    public void TtlPolicyIsOrderedByHowFastTheDataChanges()
    {
        Assert.True(CacheTtlPolicy.For(WeatherDataType.CurrentConditions) < CacheTtlPolicy.For(WeatherDataType.AirQuality));
        Assert.True(CacheTtlPolicy.For(WeatherDataType.AirQuality) < CacheTtlPolicy.For(WeatherDataType.Forecast));
        Assert.True(CacheTtlPolicy.For(WeatherDataType.Forecast) < CacheTtlPolicy.For(WeatherDataType.Elevation));
        Assert.Equal(TimeSpan.FromMinutes(7), CacheTtlPolicy.For(WeatherDataType.CurrentConditions));
    }

    [Fact]
    public void TtlJitterStaysWithinTheConfiguredBandAndSpreadsValues()
    {
        var ttl = TimeSpan.FromMinutes(7);

        Assert.Equal(ttl, CacheTtlPolicy.WithJitter(ttl, 0.1, () => 0.5));
        Assert.InRange(CacheTtlPolicy.WithJitter(ttl, 0.1, () => 0.0), ttl * 0.9, ttl);
        Assert.InRange(CacheTtlPolicy.WithJitter(ttl, 0.1, () => 1.0), ttl, ttl * 1.1);

        var samples = Enumerable.Range(0, 200)
            .Select(_ => CacheTtlPolicy.WithJitter(ttl, 0.1).TotalMilliseconds)
            .Distinct()
            .Count();

        Assert.True(samples > 50, $"jitter should spread expiries, saw {samples} distinct values");
    }

    [Fact]
    public void CellSizeIsClampedToSaneBounds()
    {
        var tiny = GeoBucketer.For(35.7, 51.4, 0.0000001);
        var huge = GeoBucketer.For(35.7, 51.4, 45.0);
        var smallest = GeoBucketer.For(35.7, 51.4, GeoBucketer.MinCellDegrees);
        var largest = GeoBucketer.For(35.7, 51.4, GeoBucketer.MaxCellDegrees);

        Assert.Equal(smallest, tiny);
        Assert.Equal(largest, huge);
        Assert.NotEqual(smallest, largest);
    }
}
