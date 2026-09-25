namespace NorthLife.Api.Authentication;

public static class AuthPolicies
{
    /// <summary>Admin role and a session that passed the TOTP step.</summary>
    public const string AdminWithMfa = "AdminWithMfa";
}
