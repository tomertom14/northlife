using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Identity;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

/// <summary>Two-factor enrollment for the signed-in account.</summary>
[ApiController]
[Route("api/auth/security")]
[Authorize]
public sealed class SecurityController(
    AppDbContext dbContext,
    TotpService totp,
    AuthTokenService tokenService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SecurityStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var googleLinked = await dbContext.ExternalLogins.AnyAsync(
            login => login.UserId == user.Id && login.Provider == AuthService.GoogleProvider,
            cancellationToken);
        return Ok(new SecurityStatusResponse(
            user.EmailConfirmed,
            user.TotpEnabled,
            await totp.RemainingRecoveryCodesAsync(user.Id, cancellationToken),
            user.PasswordHash.Length > 0,
            googleLinked));
    }

    [HttpPost("totp/setup")]
    public async Task<ActionResult<TotpSetupResponse>> BeginSetup(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var setup = await totp.BeginSetupAsync(user, cancellationToken);
        return Ok(new TotpSetupResponse(setup.Secret, setup.ProvisioningUri, setup.QrCodeDataUri));
    }

    /// <summary>Proves the authenticator works, turns 2FA on and returns a session that counts as verified.</summary>
    [HttpPost("totp/enable")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TotpEnabledResponse>> Enable(TotpCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var codes = await totp.ConfirmSetupAsync(user, request.Code ?? string.Empty, cancellationToken);
        if (codes is null)
        {
            return CodeProblem("הקוד אינו נכון. בדקו שהשעה במכשיר מעודכנת ונסו קוד חדש.");
        }

        return Ok(new TotpEnabledResponse(codes, tokenService.Create(user, mfaVerified: true)));
    }

    /// <summary>Business owners may turn 2FA off with a valid code; it stays mandatory for administrators.</summary>
    [HttpPost("totp/disable")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Disable(TotpCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        if (user.Role == UserRole.Admin)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "אימות דו-שלבי הוא חובה לחשבונות מנהל." };
            problem.Extensions["code"] = "mfa_mandatory";
            return StatusCode(StatusCodes.Status403Forbidden, problem);
        }

        try
        {
            if (!await totp.VerifySecondFactorAsync(user, request.Code ?? string.Empty, cancellationToken))
            {
                return CodeProblem("הקוד אינו נכון או שכבר נעשה בו שימוש.");
            }
        }
        catch (SecondFactorLockedException)
        {
            var locked = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "נרשמו יותר מדי קודים שגויים. נסו שוב בעוד 15 דקות." };
            locked.Extensions["code"] = "mfa_locked";
            return StatusCode(StatusCodes.Status429TooManyRequests, locked);
        }

        await totp.DisableAsync(user, cancellationToken);
        return Ok(tokenService.Create(user));
    }

    private async Task<AppUser?> CurrentUserAsync(CancellationToken cancellationToken) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
            ? await dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            : null;

    private ObjectResult CodeProblem(string message)
    {
        var details = new ValidationProblemDetails(new Dictionary<string, string[]> { ["code"] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "האימות נכשל.",
        };
        details.Extensions["code"] = "invalid_code";
        return BadRequest(details);
    }
}
