namespace NorthLife.Api.Ranking;

public readonly record struct GeoBounds(double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)
{
    public double CenterLatitude => (MinLatitude + MaxLatitude) / 2;
    public double CenterLongitude => (MinLongitude + MaxLongitude) / 2;
}

/// <summary>
/// Geohash (Niemeyer, 2008): the world is halved alternately by longitude and latitude, and the bits
/// are written five at a time in base 32. Nearby points share long prefixes, so a B-tree index on
/// the hash answers "everything in this cell" as a range scan, and a cell plus its eight neighbours
/// covers a whole neighbourhood even when the point sits near a cell edge.
/// <para>Cell size at the latitude of the Galilee (about 33°N): precision 6 ≈ 1.0 × 0.6 km,
/// 5 ≈ 4.1 × 4.9 km, 4 ≈ 33 × 20 km, 3 ≈ 131 × 156 km.</para>
/// </summary>
public static class Geohash
{
    public const string Alphabet = "0123456789bcdefghjkmnpqrstuvwxyz";
    public const int StoredPrecision = 9;
    private static readonly int[] Lookup = BuildLookup();

    public static string Encode(double latitude, double longitude, int precision = StoredPrecision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 12);
        if (latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(latitude));
        if (longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(longitude));

        double minLat = -90, maxLat = 90, minLon = -180, maxLon = 180;
        var chars = new char[precision];
        var evenBit = true;
        for (var index = 0; index < precision; index++)
        {
            var value = 0;
            for (var bit = 0; bit < 5; bit++)
            {
                if (evenBit)
                {
                    var middle = (minLon + maxLon) / 2;
                    if (longitude >= middle) { value = (value << 1) | 1; minLon = middle; }
                    else { value <<= 1; maxLon = middle; }
                }
                else
                {
                    var middle = (minLat + maxLat) / 2;
                    if (latitude >= middle) { value = (value << 1) | 1; minLat = middle; }
                    else { value <<= 1; maxLat = middle; }
                }

                evenBit = !evenBit;
            }

            chars[index] = Alphabet[value];
        }

        return new string(chars);
    }

    public static GeoBounds Decode(string hash)
    {
        double minLat = -90, maxLat = 90, minLon = -180, maxLon = 180;
        var evenBit = true;
        foreach (var character in hash)
        {
            var value = character < Lookup.Length ? Lookup[character] : -1;
            if (value < 0) throw new FormatException($"'{character}' is not a geohash character.");
            for (var bit = 4; bit >= 0; bit--)
            {
                var set = ((value >> bit) & 1) == 1;
                if (evenBit)
                {
                    var middle = (minLon + maxLon) / 2;
                    if (set) minLon = middle; else maxLon = middle;
                }
                else
                {
                    var middle = (minLat + maxLat) / 2;
                    if (set) minLat = middle; else maxLat = middle;
                }

                evenBit = !evenBit;
            }
        }

        return new GeoBounds(minLat, maxLat, minLon, maxLon);
    }

    /// <summary>The cell itself and its eight neighbours (fewer at the poles), each encoded at the same precision.</summary>
    public static IReadOnlyList<string> CellAndNeighbours(string hash)
    {
        var bounds = Decode(hash);
        var height = bounds.MaxLatitude - bounds.MinLatitude;
        var width = bounds.MaxLongitude - bounds.MinLongitude;
        var cells = new List<string>(9);
        for (var dy = -1; dy <= 1; dy++)
        {
            var latitude = bounds.CenterLatitude + dy * height;
            if (latitude is <= -90 or >= 90) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var longitude = bounds.CenterLongitude + dx * width;
                // Wrap around the antimeridian.
                if (longitude > 180) longitude -= 360;
                if (longitude < -180) longitude += 360;
                var cell = Encode(latitude, longitude, hash.Length);
                if (!cells.Contains(cell)) cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// Smallest distance from a point inside <paramref name="hash"/>'s cell to the outside of the 3×3
    /// block around it: every point closer than this is inside the block, whatever the point's
    /// position in its cell. Measured conservatively along the shorter cell side.
    /// </summary>
    public static double GuaranteedRadiusKm(string hash)
    {
        var bounds = Decode(hash);
        var heightDegrees = bounds.MaxLatitude - bounds.MinLatitude;
        var heightKm = heightDegrees * 110.57;
        // Longitude degrees shrink towards the pole; use the block's poleward edge, one cell beyond.
        var latitude = Math.Min(90, Math.Max(Math.Abs(bounds.MinLatitude), Math.Abs(bounds.MaxLatitude)) + heightDegrees);
        var widthKm = (bounds.MaxLongitude - bounds.MinLongitude) * 111.32 * Math.Cos(latitude * Math.PI / 180);
        // A 1% margin covers the gap between these flat approximations and the haversine distance.
        return 0.99 * Math.Min(heightKm, widthKm);
    }

    /// <summary>The smallest string greater than every string that starts with <paramref name="prefix"/>, in byte order.</summary>
    public static string PrefixUpperBound(string prefix) => prefix[..^1] + (char)(prefix[^1] + 1);

    private static int[] BuildLookup()
    {
        var lookup = Enumerable.Repeat(-1, 128).ToArray();
        for (var index = 0; index < Alphabet.Length; index++) lookup[Alphabet[index]] = index;
        return lookup;
    }
}
