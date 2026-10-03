namespace NorthLife.Api.Analytics;

/// <summary>
/// Time-decayed popularity, pop(t) = Σ wᵢ · e^(−λ(t − tᵢ)), with a six-hour half-life
/// (λ = ln 2 / 6 h): an interaction counts half as much after six hours and a quarter after twelve.
/// <para>
/// Stored with forward decay (Cormode, Shkapenyuk, Srivastava and Xu, 2009). For a fixed landmark L,
/// pop(t) = e^(−λ(t − L)) · Σ wᵢ · e^(λ(tᵢ − L)). The sum on the right never decays, so a new
/// interaction only adds to it and nothing has to be rewritten as time passes. The factor in front
/// is the same for every event, so ordering events by the stored sum is the same as ordering them by
/// pop(t) at any moment, and a plain B-tree index on it serves "most popular now".
/// </para>
/// <para>
/// The sum grows like e^(λt), about 2^1460 after a year, beyond the range of a double, so it is kept
/// as a natural logarithm and combined with log-sum-exp.
/// </para>
/// </summary>
public static class DecayedPopularity
{
    public static readonly TimeSpan HalfLife = TimeSpan.FromHours(6);

    public static readonly DateTimeOffset Landmark = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Decay rate per hour.</summary>
    public static readonly double Lambda = Math.Log(2) / HalfLife.TotalHours;

    /// <summary>log(w · e^(λ(t − L))): one interaction's contribution in log space.</summary>
    public static double LogContribution(double weight, DateTimeOffset occurredAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        return Math.Log(weight) + Lambda * (occurredAt - Landmark).TotalHours;
    }

    /// <summary>log(e^a + e^b) without overflow. Negative infinity stands for an empty sum.</summary>
    public static double LogAddExp(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;
        var max = Math.Max(a, b);
        return max + Math.Log(1 + Math.Exp(-Math.Abs(a - b)));
    }

    /// <summary>pop(now) from the stored log sum.</summary>
    public static double ScoreAt(double logSum, DateTimeOffset now) =>
        double.IsNegativeInfinity(logSum) ? 0 : Math.Exp(logSum - Lambda * (now - Landmark).TotalHours);
}
