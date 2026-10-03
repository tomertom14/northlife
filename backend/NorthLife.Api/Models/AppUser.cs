namespace NorthLife.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }

    /// <summary>Empty for accounts that only sign in with an external provider.</summary>
    public required string PasswordHash { get; set; }
    public required string Phone { get; set; }
    public required string BusinessName { get; set; }
    public UserRole Role { get; set; } = UserRole.BusinessOwner;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? EmailConfirmedAtUtc { get; set; }

    /// <summary>Active TOTP secret, encrypted with ASP.NET Data Protection.</summary>
    public string? TotpSecretProtected { get; set; }

    /// <summary>Secret issued during enrollment, promoted once the user proves a code.</summary>
    public string? TotpPendingSecretProtected { get; set; }
    public DateTimeOffset? TotpEnabledAtUtc { get; set; }

    /// <summary>Last accepted 30-second step; rejects replay of the same code.</summary>
    public long? TotpLastUsedStep { get; set; }

    /// <summary>
    /// Copied into every JWT. Rotating it (suspension, role or credential change) invalidates
    /// all tokens issued before, even though JWTs are otherwise stateless.
    /// </summary>
    public string SecurityStamp { get; set; } = NewSecurityStamp();
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public string? SuspensionReason { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    public void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    public bool Suspended => SuspendedAtUtc is not null;

    public ICollection<Event> Events { get; set; } = [];
    public ICollection<Place> Places { get; set; } = [];
    public ICollection<EventImage> UploadedImages { get; set; } = [];
    public ICollection<ExternalLogin> ExternalLogins { get; set; } = [];
    public ICollection<UserToken> Tokens { get; set; } = [];
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = [];

    public bool EmailConfirmed => EmailConfirmedAtUtc is not null;
    public bool TotpEnabled => TotpEnabledAtUtc is not null && TotpSecretProtected is not null;
}
