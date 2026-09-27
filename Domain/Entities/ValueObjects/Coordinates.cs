namespace Domain.Entities.ValueObjects;

public readonly record struct Coordinates(double Latitude, double Longitude)
{
    public static Coordinates FromDegrees(double lat, double lon)
    {
        if (lat < -90 || lat > 90) throw new ArgumentOutOfRangeException(nameof(lat), "Latitude must be between -90 and 90");
        if (lon < -180 || lon > 180) throw new ArgumentOutOfRangeException(nameof(lon), "Longitude must be between -180 and 180");
        return new Coordinates(lat, lon);
    }

    public override string ToString() => $"{Latitude:F6},{Longitude:F6}";

    public string ToCacheBucket() => FormattableString.Invariant($"{Math.Round(Latitude, 2):0.##}:{Math.Round(Longitude, 2):0.##}");
}