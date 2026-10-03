using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using QRCoder;

namespace NorthLife.Api.Identity;

public sealed record TotpSetup(string Secret, string ProvisioningUri, string QrCodeDataUri);

/// <summary>TOTP enrollment and second-factor checks. Secrets are encrypted at rest with Data Protection.</summary>
public sealed class TotpService(
    AppDbContext dbContext,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    Authentication.ISessionValidator sessions,
    SecondFactorThrottle throttle)
{
    public const int RecoveryCodeCount = 10;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("NorthLife.Totp.v1");

    public async Task<TotpSetup> BeginSetupAsync(AppUser user, CancellationToken cancellationToken)
    {
        var secret = Totp.NewSecret();
        var encoded = Base32.Encode(secret);
        user.TotpPendingSecretProtected = _protector.Protect(encoded);
        await dbContext.SaveChangesAsync(cancellationToken);

        var uri = Totp.ProvisioningUri("NorthLife", user.Email, secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return new TotpSetup(Group(encoded), uri, $"data:image/png;base64,{Convert.ToBase64String(png)}");
    }

    /// <summary>Activates the pending secret once the user proves a code; returns fresh backup codes.</summary>
    public async Task<IReadOnlyList<string>?> ConfirmSetupAsync(AppUser user, string code, CancellationToken cancellationToken)
    {
        if (user.TotpPendingSecretProtected is null) return null;
        var secret = Base32.Decode(_protector.Unprotect(user.TotpPendingSecretProtected));
        var step = Totp.Verify(secret, code, timeProvider.GetUtcNow(), lastUsedStep: null);
        if (step is null) return null;

        user.TotpSecretProtected = user.TotpPendingSecretProtected;
        user.TotpPendingSecretProtected = null;
        user.TotpEnabledAtUtc = timeProvider.GetUtcNow();
        user.TotpLastUsedStep = step;
        // Sessions opened before 2FA (possibly by someone else) end; the caller issues a fresh token.
        user.RotateSecurityStamp();

        await dbContext.RecoveryCodes.Where(existing => existing.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        var codes = new List<string>(RecoveryCodeCount);
        for (var index = 0; index < RecoveryCodeCount; index++)
        {
            var (plain, hash) = SecureTokens.NewRecoveryCode();
            codes.Add(plain);
            dbContext.RecoveryCodes.Add(new RecoveryCode { UserId = user.Id, CodeHash = hash });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        sessions.Invalidate(user.Id);
        return codes;
    }

    /// <summary>
    /// Accepts a current TOTP code (each step once) or an unused backup code. Throws
    /// <see cref="SecondFactorLockedException"/> once the account has too many recent wrong codes.
    /// </summary>
    public async Task<bool> VerifySecondFactorAsync(AppUser user, string code, CancellationToken cancellationToken)
    {
        if (!user.TotpEnabled) return false;
        if (throttle.IsLocked(user.Id)) throw new SecondFactorLockedException();

        var accepted = await CheckCodeAsync(user, code.Trim(), cancellationToken);
        if (accepted) throttle.Reset(user.Id);
        else throttle.RecordFailure(user.Id);
        return accepted;
    }

    private async Task<bool> CheckCodeAsync(AppUser user, string code, CancellationToken cancellationToken)
    {
        if (code.Length == Totp.DefaultDigits && code.All(char.IsAsciiDigit))
        {
            var secret = Base32.Decode(_protector.Unprotect(user.TotpSecretProtected!));
            var step = Totp.Verify(secret, code, timeProvider.GetUtcNow(), user.TotpLastUsedStep);
            if (step is null) return false;
            user.TotpLastUsedStep = step;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var hash = SecureTokens.Hash(SecureTokens.NormalizeRecoveryCode(code));
        var now = timeProvider.GetUtcNow();
        var used = await dbContext.RecoveryCodes
            .Where(candidate => candidate.UserId == user.Id && candidate.CodeHash == hash && candidate.UsedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.UsedAtUtc, now), cancellationToken);
        return used == 1;
    }

    public async Task DisableAsync(AppUser user, CancellationToken cancellationToken)
    {
        user.TotpSecretProtected = null;
        user.TotpPendingSecretProtected = null;
        user.TotpEnabledAtUtc = null;
        user.TotpLastUsedStep = null;
        user.RotateSecurityStamp();
        await dbContext.RecoveryCodes.Where(existing => existing.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        sessions.Invalidate(user.Id);
    }

    public Task<int> RemainingRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.RecoveryCodes.CountAsync(code => code.UserId == userId && code.UsedAtUtc == null, cancellationToken);

    // "JBSW Y3DP EHPK 3PXP": easier to type into an authenticator by hand.
    private static string Group(string secret) =>
        string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4)
            .Select(index => secret.Substring(index * 4, Math.Min(4, secret.Length - index * 4))));
}
