using Npgsql;

namespace NorthLife.Api.Data;

/// <summary>
/// Converts a postgresql:// URL (the form Render and most hosts publish) into an Npgsql
/// connection string, which Npgsql itself does not accept as a URL.
/// </summary>
public static class DatabaseUrl
{
    public static string ToConnectionString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql") ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException(
                "Database:Url must be a postgres:// or postgresql:// URL.");
        }

        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            SslMode = SslMode.Prefer,
        }.ConnectionString;
    }
}
