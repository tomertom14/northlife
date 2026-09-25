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

    public ICollection<Event> Events { get; set; } = [];
    public ICollection<EventImage> UploadedImages { get; set; } = [];
    public ICollection<ExternalLogin> ExternalLogins { get; set; } = [];
    public ICollection<UserToken> Tokens { get; set; } = [];
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = [];

    public bool EmailConfirmed => EmailConfirmedAtUtc is not null;
    public bool TotpEnabled => TotpEnabledAtUtc is not null && TotpSecretProtected is not null;
}
