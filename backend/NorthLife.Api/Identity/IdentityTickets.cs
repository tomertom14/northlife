using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NorthLife.Api.Identity;

/// <summary>
/// Short-lived, encrypted and signed hand-over tokens between sign-in steps:
/// "password accepted, now prove the second factor" and "Google verified you, now finish your profile".
/// </summary>
public sealed class IdentityTickets(IDataProtectionProvider dataProtection)
{
    public static readonly TimeSpan MfaLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SignupLifetime = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector _mfa =
        dataProtection.CreateProtector("NorthLife.MfaTicket.v1").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector _signup =
        dataProtection.CreateProtector("NorthLife.GoogleSignup.v1").ToTimeLimitedDataProtector();

    public string IssueMfa(Guid userId) => _mfa.Protect(userId.ToString("N"), MfaLifetime);

    public Guid? ReadMfa(string? ticket)
    {
        var payload = Unprotect(_mfa, ticket);
        return Guid.TryParseExact(payload, "N", out var userId) ? userId : null;
    }

    public string IssueSignup(GoogleIdentity identity) =>
        _signup.Protect(JsonSerializer.Serialize(identity), SignupLifetime);

    public GoogleIdentity? ReadSignup(string? ticket)
    {
        var payload = Unprotect(_signup, ticket);
        return payload is null ? null : JsonSerializer.Deserialize<GoogleIdentity>(payload);
    }

    private static string? Unprotect(ITimeLimitedDataProtector protector, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 4000) return null;
        try
        {
            return protector.Unprotect(ticket);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
