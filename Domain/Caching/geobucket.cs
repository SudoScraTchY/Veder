using System.Globalization;

namespace Domain.Caching;

/// <summary>
/// A proximity cell on a fixed latitude/longitude grid. Two requests in the same cell (or in an
/// adjacent one, when the neighbourhood lookup is used) share a cache entry, so repeatedly asking
/// about the same neighbourhood does not cost an upstream call.
/// </summary>
public readonly record struct GeoBucket(int LatitudeCell, int LongitudeCell)
{
    /// <summary>Culture-invariant so a host running under <c>fa-IR</c> or <c>de-DE</c> cannot inject a decimal comma into a cache key.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{LatitudeCell}:{LongitudeCell}");
}

/// <summary>
/// Derives proximity cells, and deliberately not by "rounding the coordinate": rounding puts two
/// points two metres apart on opposite sides of a cell edge (a structural discontinuity that finer
/// precision only relocates), it produces <c>-0.000</c> for small negative latitudes, it breaks at
/// the antimeridian, and it is meaningless at the poles where a degree of longitude collapses.
/// Floor-to-grid plus a neighbour lookup handles all four.
/// </summary>
public static class GeoBucketer
{
    /// <summary>Smallest accepted cell: ~550 m at the equator. Anything finer is not a proximity bucket any more.</summary>
    public const double MinCellDegrees = 0.005;

    /// <summary>Largest accepted cell: 1°, ~111 km.</summary>
    public const double MaxCellDegrees = 1.0;

    /// <summary>City-scale default: 0.02°, roughly 2 km.</summary>
    public const double DefaultCellDegrees = 0.02;

    /// <summary>Above this latitude a degree of longitude is under ~29 km, so longitude bucketing is skipped.</summary>
    public const double PolarLatitudeThreshold = 75.0;

    public const int LatitudeCells = 180;
    public const int LongitudeCells = 360;

    public static GeoBucket For(double latitude, double longitude, double cellSizeDegrees = DefaultCellDegrees)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be a finite number.");
        }

        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be a finite number.");
        }

        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90.");
        }

        var cell = NormalizeCellSize(cellSizeDegrees);
        var normalizedLatitude = NormalizeNegativeZero(latitude);
        var latitudeCells = (int)Math.Round(LatitudeCells / cell, MidpointRounding.AwayFromZero);

        var latitudeCell = Math.Clamp(
            (int)Math.Floor((normalizedLatitude + 90.0) / cell),
            0,
            latitudeCells - 1);

        // Near a pole every longitude is the same place, so collapse them onto one cell.
        if (Math.Abs(normalizedLatitude) >= PolarLatitudeThreshold)
        {
            return new GeoBucket(latitudeCell, 0);
        }

        var longitudeCells = (int)Math.Round(LongitudeCells / cell, MidpointRounding.AwayFromZero);
        var longitudeCell = (int)Math.Floor((WrapLongitude(longitude) + 180.0) / cell);
        longitudeCell = ((longitudeCell % longitudeCells) + longitudeCells) % longitudeCells;

        return new GeoBucket(latitudeCell, longitudeCell);
    }

    /// <summary>
    /// The cell containing the coordinate plus its eight neighbours, de-duplicated. A lookup walking
    /// this set turns the cell-edge discontinuity into a cache hit instead of a miss.
    /// </summary>
    public static IReadOnlyList<GeoBucket> Neighbourhood(double latitude, double longitude, double cellSizeDegrees = DefaultCellDegrees)
    {
        var cell = NormalizeCellSize(cellSizeDegrees);
        var centre = For(latitude, longitude, cell);
        var latitudeCells = (int)Math.Round(LatitudeCells / cell, MidpointRounding.AwayFromZero);
        var longitudeCells = (int)Math.Round(LongitudeCells / cell, MidpointRounding.AwayFromZero);
        var polar = Math.Abs(NormalizeNegativeZero(latitude)) >= PolarLatitudeThreshold;

        var buckets = new List<GeoBucket>(polar ? 3 : 9);

        for (var deltaLatitude = -1; deltaLatitude <= 1; deltaLatitude++)
        {
            var latitudeCell = centre.LatitudeCell + deltaLatitude;
            if (latitudeCell < 0 || latitudeCell >= latitudeCells)
            {
                continue;
            }

            if (polar)
            {
                buckets.Add(new GeoBucket(latitudeCell, 0));
                continue;
            }

            for (var deltaLongitude = -1; deltaLongitude <= 1; deltaLongitude++)
            {
                var longitudeCell = centre.LongitudeCell + deltaLongitude;
                longitudeCell = ((longitudeCell % longitudeCells) + longitudeCells) % longitudeCells;
                buckets.Add(new GeoBucket(latitudeCell, longitudeCell));
            }
        }

        return buckets.Distinct().ToList();
    }

    /// <summary>1 for the immediate cell plus its neighbours.</summary>
    public static int RegionSize(int regionLevel) => regionLevel <= 0 ? 1 : (2 * regionLevel + 1) * (2 * regionLevel + 1);

    /// <summary>Maps any longitude, including ±180 and out-of-range values, into <c>[-180, 180)</c>.</summary>
    public static double WrapLongitude(double longitude)
    {
        var wrapped = ((longitude + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

        // Guards the seam: the modulo arithmetic can surface -180.0 for the +180 input.
        if (Math.Abs(wrapped + 180.0) < 1e-9)
        {
            wrapped = -180.0;
        }

        return NormalizeNegativeZero(wrapped);
    }

    /// <summary>Collapses <c>-0.0</c> onto <c>0.0</c> so both never produce different cache keys.</summary>
    public static double NormalizeNegativeZero(double value) => value == 0d ? 0d : value;

    private static double NormalizeCellSize(double cellSizeDegrees) =>
        double.IsNaN(cellSizeDegrees) ? DefaultCellDegrees : Math.Clamp(cellSizeDegrees, MinCellDegrees, MaxCellDegrees);
}
