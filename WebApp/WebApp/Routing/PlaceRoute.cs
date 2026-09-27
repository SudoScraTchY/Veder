using System.Globalization;

namespace WebApp.Routing;

/// <summary>
/// The canonical URL shape for this site. The domain is the product, so a place needs no /weather
/// prefix: identity lives in the path and options live in the query string, which keeps a reading
/// shareable, bookmarkable and indexable.
///     /Tehran                   a place
///     /35.6892,51.389           coordinates
///     /Tehran?providerId=...    options stay in the query
/// </summary>
public static class PlaceRoute
{
    public static string ForPlace(string place) => "/" + ToSlug(place);

    public static string ForCoordinates(double latitude, double longitude) =>
        "/" + FormatCoordinate(latitude) + "," + FormatCoordinate(longitude);

    /// <summary>Spaces become dashes; everything else is escaped for the URL.</summary>
    public static string ToSlug(string place) => Uri.EscapeDataString(place.Trim().Replace(' ', '-'));

    /// <summary>The inverse of <see cref="ToSlug"/>, used as the geocoding query.</summary>
    public static string ToDisplayName(string slug)
    {
        var decoded = Uri.UnescapeDataString(slug).Replace('-', ' ');
        return string.Join(' ', decoded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Coordinates must be culture-invariant: under a Persian locale an interpolated double writes
    /// 35,6892, and the comma is the very separator this route uses.
    /// </summary>
    public static string FormatCoordinate(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}