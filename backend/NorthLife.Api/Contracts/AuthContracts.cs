using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string Phone,
    string BusinessName);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    AuthUserResponse User);

public sealed record AuthUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string BusinessName,
    UserRole Role);
