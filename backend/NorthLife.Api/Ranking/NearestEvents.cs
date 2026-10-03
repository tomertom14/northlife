namespace NorthLife.Api.Ranking;

public sealed record GeoCandidate<T>(T Item, double Latitude, double Longitude);

public sealed record NearestResult<T>(IReadOnlyList<(T Item, double DistanceKm)> Items, int Precision, int CellsScanned, double GuaranteedRadiusKm);

/// <summary>
/// k nearest events with geohash cells. Start with the visitor's precision-6 cell and its eight
/// neighbours (about 3 × 2 km around them), measure every candidate exactly with the haversine
/// formula, and accept the k closest once they all lie within the block's guaranteed radius: any
/// event outside the block is farther than that, so the answer is exact. Otherwise widen the ring by
/// dropping one precision level (each level is 8 to 32 times the area) until k are found or the
/// block exceeds the maximum radius. Each step is nine B-tree range scans, O(9 log n + m).
/// </summary>
public static class NearestEvents
{
    public const int FinestPrecision = 6;
    public const int CoarsestPrecision = 2;

    public static async Task<NearestResult<T>> FindAsync<T>(
        double latitude,
        double longitude,
        int k,
        double maxRadiusKm,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<GeoCandidate<T>>>> fetchByPrefixes)
    {
        var cellsScanned = 0;
        List<(T Item, double DistanceKm)> ranked = [];
        for (var precision = FinestPrecision; precision >= CoarsestPrecision; precision--)
        {
            var cell = Geohash.Encode(latitude, longitude, precision);
            var cells = Geohash.CellAndNeighbours(cell);
            cellsScanned += cells.Count;
            var candidates = await fetchByPrefixes(cells);
            ranked = candidates
                .Select(candidate => (candidate.Item, DistanceKm: Haversine.DistanceKm(latitude, longitude, candidate.Latitude, candidate.Longitude)))
                .OrderBy(pair => pair.DistanceKm)
                .ToList();

            var guaranteed = Geohash.GuaranteedRadiusKm(cell);
            var inside = ranked.TakeWhile(pair => pair.DistanceKm <= guaranteed).Count();
            if (inside >= k)
            {
                // The k closest are all inside the guaranteed radius: exact.
                return new NearestResult<T>(ranked.Take(k).ToList(), precision, cellsScanned, guaranteed);
            }

            if (guaranteed >= maxRadiusKm || precision == CoarsestPrecision)
            {
                // Fewer than k events within the maximum radius; the block covers that whole circle.
                return new NearestResult<T>(ranked.Where(pair => pair.DistanceKm <= maxRadiusKm).Take(k).ToList(), precision, cellsScanned, guaranteed);
            }
        }

        return new NearestResult<T>(ranked.Take(k).ToList(), CoarsestPrecision, cellsScanned, 0);
    }
}

public static class Haversine
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
