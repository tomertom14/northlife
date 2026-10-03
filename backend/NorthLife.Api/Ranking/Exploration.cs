using System.Linq.Expressions;
using NorthLife.Api.Models;

namespace NorthLife.Api.Ranking;

public static class Exploration
{
    /// <summary>
    /// Randomised top-N (Joachims, Swaminathan and Schnabel, 2017): a Fisher-Yates shuffle of the
    /// first <paramref name="depth"/> items. Done on a small share of "hot" pages, it shows the same
    /// events at different positions to the same audience, which is what lets the click model tell
    /// position bias apart from appeal. The cost is a slightly less sorted top of the page, now and
    /// then.
    /// </summary>
    public static void ShuffleTop<T>(IList<T> items, int depth, Random random)
    {
        var count = Math.Min(depth, items.Count);
        for (var index = count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}

public static class GeohashFilter
{
    /// <summary>
    /// events whose geohash starts with any of the prefixes, written as byte-order ranges
    /// (prefix &lt;= geohash &lt; next prefix) so each prefix is one B-tree range scan.
    /// </summary>
    public static Expression<Func<Event, bool>> StartsWithAny(IReadOnlyList<string> prefixes) =>
        StartsWithAny<Event>(prefixes);

    /// <summary>The same filter for any entity with a byte-order <c>Geohash</c> column (events and places).</summary>
    public static Expression<Func<T, bool>> StartsWithAny<T>(IReadOnlyList<string> prefixes)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var hash = Expression.Property(parameter, nameof(Event.Geohash));
        var compare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;
        Expression? body = null;
        foreach (var prefix in prefixes.Distinct())
        {
            var lower = Expression.GreaterThanOrEqual(
                Expression.Call(compare, hash, Expression.Constant(prefix)),
                Expression.Constant(0));
            var upper = Expression.LessThan(
                Expression.Call(compare, hash, Expression.Constant(Geohash.PrefixUpperBound(prefix))),
                Expression.Constant(0));
            var range = Expression.AndAlso(lower, upper);
            body = body is null ? range : Expression.OrElse(body, range);
        }

        return Expression.Lambda<Func<T, bool>>(body ?? Expression.Constant(false), parameter);
    }
}
