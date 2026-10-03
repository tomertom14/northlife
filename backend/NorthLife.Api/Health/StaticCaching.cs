using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace NorthLife.Api.Health;

/// <summary>
/// Cache lifetimes for the Angular files. The build names every script and stylesheet after a hash of its content
/// ("main-SONQTXSQ.js"), so those can be cached for a year and never revalidated; index.html must always be
/// revalidated so a new deployment is picked up at once; everything else (icons, illustrations) gets an hour.
/// </summary>
public static partial class StaticCaching
{
    public const string Immutable = "public,max-age=31536000,immutable";
    public const string Revalidate = "no-cache";
    public const string Short = "public,max-age=3600";

    public static void Apply(StaticFileResponseContext context) =>
        context.Context.Response.Headers.CacheControl = For(context.File.Name);

    public static string For(string fileName)
    {
        if (string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase)) return Revalidate;
        return HashedName().IsMatch(fileName) ? Immutable : Short;
    }

    // "-" followed by the eight-character hash Angular's build appends, before the extension.
    [GeneratedRegex(@"-[A-Za-z0-9_]{8}\.(js|css|woff2?|ttf|svg|png|jpe?g|webp|avif)$")]
    private static partial Regex HashedName();
}
