using NorthLife.Api.Data;
using Npgsql;

namespace NorthLife.Tests;

public sealed class DatabaseUrlTests
{
    [Fact]
    public void Render_style_url_becomes_an_npgsql_connection_string()
    {
        var connectionString = DatabaseUrl.ToConnectionString(
            "postgresql://northlife_user:p%40ss%3Aword@dpg-abc123-a:5433/northlife_db");

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("dpg-abc123-a", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("northlife_db", parsed.Database);
        Assert.Equal("northlife_user", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
    }

    [Fact]
    public void Missing_port_defaults_to_postgres_port()
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            DatabaseUrl.ToConnectionString("postgres://user:secret@db-host/app"));

        Assert.Equal(5432, parsed.Port);
    }

    [Theory]
    [InlineData("Host=localhost;Database=app")]
    [InlineData("mysql://user:secret@host/app")]
    public void Non_postgres_urls_are_rejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseUrl.ToConnectionString(value));
    }
}
