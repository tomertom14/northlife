using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace NorthLife.Api.Identity;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>OAuth 2.0 Web client ID; empty disables Google sign-in.</summary>
    public string ClientId { get; init; } = string.Empty;
}

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string Name);

public interface IGoogleTokenValidator
{
    bool Enabled { get; }
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>Checks a Google ID token's signature, issuer, expiry and audience (our client ID).</summary>
public sealed class GoogleTokenValidator(IOptions<GoogleOptions> options) : IGoogleTokenValidator
{
    public bool Enabled => !string.IsNullOrWhiteSpace(options.Value.ClientId);

    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(idToken) || idToken.Length > 4096) return null;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [options.Value.ClientId] });
            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified, payload.Name ?? payload.Email);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}

public enum GoogleSignInDecision
{
    /// <summary>This Google account is already linked: sign in.</summary>
    SignInLinked,

    /// <summary>A NorthLife account has the same, Google-verified email: link it, then sign in.</summary>
    LinkExisting,

    /// <summary>New person: collect the business profile before creating the account.</summary>
    RequireProfile,

    /// <summary>Never link or create on an address Google has not verified.</summary>
    RejectUnverifiedEmail,
}

/// <summary>The account-linking rule, kept pure so every branch is unit-tested.</summary>
public static class GoogleAccountResolver
{
    public static GoogleSignInDecision Decide(bool hasLinkedLogin, bool accountWithEmailExists, bool emailVerified)
    {
        if (hasLinkedLogin) return GoogleSignInDecision.SignInLinked;
        if (!emailVerified) return GoogleSignInDecision.RejectUnverifiedEmail;
        return accountWithEmailExists ? GoogleSignInDecision.LinkExisting : GoogleSignInDecision.RequireProfile;
    }
}
