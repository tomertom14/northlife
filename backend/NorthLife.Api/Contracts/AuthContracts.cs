using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string Phone,
    string BusinessName);

public sealed record LoginRequest(string Email, string Password);

/// <summary>Signed in. <see cref="Status"/> lets clients tell it apart from the other sign-in outcomes.</summary>
public sealed record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    AuthUserResponse User)
{
    public string Status => "authenticated";
}

/// <summary>Password (or Google) accepted; the account needs its TOTP or backup code next.</summary>
public sealed record MfaChallengeResponse(string MfaToken)
{
    public string Status => "mfa_required";
}

/// <summary>Google verified the person, but no NorthLife account exists yet.</summary>
public sealed record GoogleProfileRequiredResponse(string SignupToken, string Email, string FullName)
{
    public string Status => "profile_required";
}

public sealed record AuthUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string BusinessName,
    UserRole Role,
    bool EmailConfirmed = false,
    bool TotpEnabled = false,
    bool MfaVerified = false);

public sealed record TokenRequest(string Token);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record MfaRequest(string MfaToken, string Code);
public sealed record GoogleSignInRequest(string IdToken);
public sealed record GoogleCompleteRequest(string SignupToken, string FullName, string BusinessName, string Phone);
public sealed record TotpCodeRequest(string Code);
public sealed record TotpSetupResponse(string Secret, string ProvisioningUri, string QrCodeDataUri);
public sealed record TotpEnabledResponse(IReadOnlyList<string> RecoveryCodes, AuthResponse Auth);
public sealed record SecurityStatusResponse(bool EmailConfirmed, bool TotpEnabled, int RecoveryCodesLeft, bool HasPassword, bool GoogleLinked);
