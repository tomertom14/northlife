using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Authentication;

public sealed class AdminBootstrapper(
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var email = Required("Email");
        var password = Required("Password");
        var fullName = configuration["BootstrapAdmin:FullName"]?.Trim() ?? "NorthLife Admin";
        var phone = configuration["BootstrapAdmin:Phone"]?.Trim() ?? "0000000000";
        var businessName = configuration["BootstrapAdmin:BusinessName"]?.Trim() ?? "NorthLife";
        var request = new Contracts.RegisterRequest(fullName, email, password, phone, businessName);
        AuthInputValidator.ValidateRegistration(request);

        var normalizedEmail = AuthService.NormalizeEmail(email);
        var existing = await dbContext.Users.SingleOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (existing is not null && existing.Role != UserRole.Admin)
        {
            throw new InvalidOperationException(
                "The bootstrap email belongs to a non-admin account; refusing role escalation.");
        }

        var admin = existing ?? new AppUser
        {
            Id = Guid.CreateVersion7(),
            FullName = fullName,
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Phone = phone,
            BusinessName = businessName,
            Role = UserRole.Admin,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        admin.FullName = fullName;
        admin.Phone = phone;
        admin.BusinessName = businessName;
        admin.PasswordHash = passwordHasher.HashPassword(admin, password);

        if (existing is null)
        {
            dbContext.Users.Add(admin);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string Required(string name) =>
        configuration[$"BootstrapAdmin:{name}"]?.Trim() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"BootstrapAdmin:{name} is required for --bootstrap-admin.");
}
