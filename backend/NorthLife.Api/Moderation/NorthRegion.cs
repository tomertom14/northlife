namespace NorthLife.Api.Moderation;

/// <summary>
/// A simplified outline of northern Israel: the Haifa coast and Carmel, the Galilee, the Golan, and the
/// Jezreel and Beit She'an valleys. It follows the Lebanese border closely enough to leave out towns
/// such as Bint Jbeil, and runs north of Jenin and Hadera. It is deliberately approximate: an event
/// just outside it is only held for a human to look at, never rejected.
/// </summary>
public static class NorthRegion
{
    /// <summary>Vertices as (latitude, longitude), clockwise from Rosh HaNikra.</summary>
    public static readonly IReadOnlyList<(double Latitude, double Longitude)> Outline =
    [
        (33.093, 35.103), // Rosh HaNikra
        (33.078, 35.160), // Shlomi
        (33.085, 35.300), // Shtula
        (33.090, 35.470), // Avivim
        (33.120, 35.550), // Yiftah
        (33.220, 35.545), // Margaliot ridge
        (33.293, 35.565), // Metula, north-west
        (33.293, 35.595), // Metula, north-east
        (33.260, 35.625), // Ghajar
        (33.340, 35.790), // Mount Hermon
        (33.250, 35.830), // Mas'ade
        (33.120, 35.850), // Quneitra line
        (32.880, 35.880), // Tel Saki
        (32.715, 35.880), // Yarmouk, east
        (32.680, 35.650), // Hamat Gader
        (32.630, 35.575), // Jordan and Yarmouk confluence
        (32.395, 35.555), // Jordan valley south of Beit She'an
        (32.470, 35.470), // Gilboa, east
        (32.520, 35.330), // north of Jenin
        (32.530, 35.200), // Umm al-Fahm
        (32.500, 35.080), // Wadi Ara, west
        (32.470, 34.870), // coast north of Hadera
        (32.840, 34.930), // sea off the Carmel
        (33.100, 35.060), // sea off Rosh HaNikra
    ];

    public static bool Contains(double latitude, double longitude) =>
        PointInPolygon.Contains(Outline, latitude, longitude);
}

public static class PointInPolygon
{
    /// <summary>
    /// Even-odd ray casting (the crossing-number test; W. R. Franklin's PNPOLY): cast a ray from the
    /// point towards growing longitude and count the polygon edges it crosses. An odd count means
    /// inside. Each edge is taken half-open in latitude, so a ray through a vertex is counted once.
    /// Latitude and longitude are treated as plane coordinates, which is accurate enough over a
    /// region 150 km across. O(number of vertices).
    /// </summary>
    public static bool Contains(IReadOnlyList<(double Latitude, double Longitude)> polygon, double latitude, double longitude)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var (latitudeA, longitudeA) = polygon[current];
            var (latitudeB, longitudeB) = polygon[previous];
            if ((latitudeA > latitude) == (latitudeB > latitude)) continue;

            var crossingLongitude = longitudeA + (latitude - latitudeA) * (longitudeB - longitudeA) / (latitudeB - latitudeA);
            if (longitude < crossingLongitude) inside = !inside;
        }

        return inside;
    }
}
