using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Identity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService, AccountService accountService, AppDbContext dbContext) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.RegisterAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (AuthValidationException exception)
        {
            return ValidationProblem(exception.Errors, "פרטי ההרשמה אינם תקינים.", "invalid_registration");
        }
        catch (AuthConflictException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "לא ניתן להשלים את ההרשמה.", exception.Code, exception.Message);
        }
    }

    /// <summary>Returns <see cref="AuthResponse"/> or, for accounts with TOTP, <see cref="MfaChallengeResponse"/>.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return SignInResult(await authService.LoginAsync(request, cancellationToken));
        }
        catch (LoginFailedException)
        {
            return Problem(StatusCodes.Status401Unauthorized, "ההתחברות נכשלה.", "invalid_credentials", "כתובת האימייל או הסיסמה אינם נכונים.");
        }        catch (AccountSuspendedException)
        {
            return Problem(StatusCodes.Status403Forbidden, "החשבון מושעה.", "account_suspended", "החשבון הושעה על ידי צוות NorthLife. לבירור פנו אלינו.");
        }
    }

    [HttpPost("mfa")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> CompleteMfa(MfaRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.CompleteMfaAsync(request, cancellationToken));
        }
        catch (LoginFailedException exception)
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                "האימות הדו-שלבי נכשל.",
                exception.Code,
                exception.Code == "mfa_expired" ? "פג תוקף שלב האימות. התחברו מחדש." : "הקוד אינו נכון או שכבר נעשה בו שימוש.");
        }
        catch (SecondFactorLockedException)
        {
            return MfaLockedProblem();
        }
        catch (AccountSuspendedException)
        {
            return Problem(StatusCodes.Status403Forbidden, "החשבון מושעה.", "account_suspended", "החשבון הושעה על ידי צוות NorthLife. לבירור פנו אלינו.");
        }
    }

    [HttpPost("google")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Google(GoogleSignInRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return SignInResult(await authService.GoogleSignInAsync(request.IdToken, cancellationToken));
        }
        catch (GoogleSignInException exception)
        {
            return GoogleProblem(exception.Code);
        }        catch (AccountSuspendedException)
        {
            return Problem(StatusCodes.Status403Forbidden, "החשבון מושעה.", "account_suspended", "החשבון הושעה על ידי צוות NorthLife. לבירור פנו אלינו.");
        }
    }

    [HttpPost("google/complete")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> CompleteGoogle(GoogleCompleteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created, await authService.CompleteGoogleSignupAsync(request, cancellationToken));
        }
        catch (GoogleSignInException exception)
        {
            return GoogleProblem(exception.Code);
        }
        catch (AuthValidationException exception)
        {
            return ValidationProblem(exception.Errors, "פרטי העסק אינם תקינים.", "invalid_registration");
        }
        catch (AuthConflictException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "לא ניתן להשלים את ההרשמה.", exception.Code, exception.Message);
        }
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, CancellationToken cancellationToken) =>
        await accountService.VerifyEmailAsync(request.Token, cancellationToken)
            ? NoContent()
            : Problem(StatusCodes.Status400BadRequest, "הקישור אינו תקף.", "invalid_token", "הקישור פג תוקף או שכבר נעשה בו שימוש.");

    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting("email")]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Problem(StatusCodes.Status401Unauthorized, "נדרשת התחברות.", "invalid_token", null);
        await accountService.ResendVerificationAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Always 204, whether or not the email has an account.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("email")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await accountService.ForgotPasswordAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await accountService.ResetPasswordAsync(request.Token, request.Password, cancellationToken)
                ? NoContent()
                : Problem(StatusCodes.Status400BadRequest, "הקישור אינו תקף.", "invalid_token", "הקישור פג תוקף או שכבר נעשה בו שימוש.");
        }
        catch (AuthValidationException exception)
        {
            return ValidationProblem(exception.Errors, "הסיסמה אינה תקינה.", "invalid_password");
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout() => NoContent();

    /// <summary>Current account state from the database, plus whether this session passed 2FA.</summary>
    [HttpGet("session")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponse>> Session(CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId))
        {
            return Problem(StatusCodes.Status401Unauthorized, "ההתחברות נכשלה.", "invalid_token", "נדרשת התחברות מחדש.");
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "ההתחברות נכשלה.", "invalid_token", "נדרשת התחברות מחדש.");
        }

        return Ok(AuthTokenService.ToUserResponse(user, User.FindFirstValue(AuthTokenService.MfaClaim) == "true"));
    }

    private IActionResult SignInResult(SignInOutcome outcome) => outcome switch
    {
        SignedIn signedIn => Ok(signedIn.Response),
        SecondFactorRequired challenge => Ok(new MfaChallengeResponse(challenge.Ticket)),
        ProfileRequired profile => Ok(new GoogleProfileRequiredResponse(profile.Ticket, profile.Email, profile.FullName)),
        _ => throw new InvalidOperationException("Unknown sign-in outcome."),
    };

    private ObjectResult GoogleProblem(string code) => code switch
    {
        "google_disabled" => Problem(StatusCodes.Status404NotFound, "התחברות עם Google אינה זמינה.", code, null),
        "signup_expired" => Problem(StatusCodes.Status400BadRequest, "פג תוקף ההרשמה.", code, "התחברו שוב עם Google כדי להמשיך."),
        "google_email_unverified" => Problem(StatusCodes.Status400BadRequest, "האימייל בחשבון Google אינו מאומת.", code, null),
        _ => Problem(StatusCodes.Status401Unauthorized, "ההתחברות עם Google נכשלה.", code, null),
    };

    private bool TryUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors, string title, string code)
    {
        var details = new ValidationProblemDetails(errors.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
        };
        details.Extensions["code"] = code;
        return StatusCode(StatusCodes.Status400BadRequest, details);
    }

    private ObjectResult MfaLockedProblem() =>
        Problem(
            StatusCodes.Status429TooManyRequests,
            "יותר מדי ניסיונות.",
            "mfa_locked",
            "נרשמו יותר מדי קודים שגויים. נסו שוב בעוד 15 דקות.");

    private ObjectResult Problem(int status, string title, string code, string? detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
