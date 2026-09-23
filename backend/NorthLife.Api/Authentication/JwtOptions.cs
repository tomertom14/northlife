namespace NorthLife.Api.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication";

    public required string JwtKey { get; init; }
    public string Issuer { get; init; } = "NorthLife";
    public string Audience { get; init; } = "NorthLife.Web";
    public int TokenLifetimeMinutes { get; init; } = 60;
}
