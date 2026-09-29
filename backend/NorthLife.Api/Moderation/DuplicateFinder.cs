using NorthLife.Api.Ranking;

namespace NorthLife.Api.Moderation;

/// <summary>An event as the duplicate check sees it: where and on which Israel date it starts.</summary>
public sealed record DuplicateCandidate(Guid Id, string Title, double Latitude, double Longitude, DateOnly LocalStartDate);

public sealed record DuplicateMatch(Guid EventId, string Title, double Similarity);

public static class DuplicateFinder
{
    /// <summary>
    /// The most similar other event that starts on the same Israel date, lies within
    /// <paramref name="maxDistanceMeters"/> (haversine), and whose text similarity reaches
    /// <paramref name="threshold"/>; null when there is none. The cheap date and distance filters run
    /// before the similarity, which comes from the TF-IDF content model (cosine of L2-normalised
    /// vectors, so an exact copy scores 1).
    /// </summary>
    public static DuplicateMatch? Find(
        DuplicateCandidate subject,
        IEnumerable<DuplicateCandidate> others,
        Func<Guid, Guid, double> similarity,
        double threshold,
        double maxDistanceMeters)
    {
        DuplicateMatch? best = null;
        foreach (var other in others)
        {
            if (other.Id == subject.Id || other.LocalStartDate != subject.LocalStartDate) continue;
            var meters = Haversine.DistanceKm(subject.Latitude, subject.Longitude, other.Latitude, other.Longitude) * 1000;
            if (meters > maxDistanceMeters) continue;

            var score = similarity(subject.Id, other.Id);
            if (score >= threshold && (best is null || score > best.Similarity))
            {
                best = new DuplicateMatch(other.Id, other.Title, score);
            }
        }

        return best;
    }
}
