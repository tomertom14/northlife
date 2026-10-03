using System.Text;

namespace NorthLife.Api.Services;

/// <summary>
/// Opaque position for keyset (seek) pagination over (created_at DESC, id DESC). Unlike OFFSET,
/// fetching page k costs O(log n + page size) on the composite index, and rows inserted meanwhile
/// never shift or duplicate items across pages.
/// </summary>
public readonly record struct KeysetCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode()
    {
        var raw = $"{CreatedAt.UtcTicks}:{Id:N}";
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static KeysetCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 100) return null;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var parts = Encoding.ASCII.GetString(Convert.FromBase64String(padded)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                return null;
            }

            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
            return new KeysetCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
