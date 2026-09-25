using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "ck_users_role",
                "role IN ('BusinessOwner', 'Admin')");
        });

        builder.HasKey(user => user.Id).HasName("pk_users");

        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.FullName).HasColumnName("full_name").HasMaxLength(150);
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(320);
        builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash");
        builder.Property(user => user.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(user => user.BusinessName).HasColumnName("business_name").HasMaxLength(200);
        builder.Property(user => user.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(30);
        builder.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(user => user.EmailConfirmedAtUtc).HasColumnName("email_confirmed_at_utc").HasColumnType("timestamptz");
        builder.Property(user => user.TotpSecretProtected).HasColumnName("totp_secret_protected").HasMaxLength(1000);
        builder.Property(user => user.TotpPendingSecretProtected).HasColumnName("totp_pending_secret_protected").HasMaxLength(1000);
        builder.Property(user => user.TotpEnabledAtUtc).HasColumnName("totp_enabled_at_utc").HasColumnType("timestamptz");
        builder.Property(user => user.TotpLastUsedStep).HasColumnName("totp_last_used_step");
        builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(32);
        builder.Property(user => user.SuspendedAtUtc).HasColumnName("suspended_at_utc").HasColumnType("timestamptz");
        builder.Property(user => user.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(500);

        // Keyset pagination for the admin user list: newest first, id as tie-breaker.
        builder.HasIndex(user => new { user.CreatedAtUtc, user.Id }).HasDatabaseName("ix_users_created_id");

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");
    }
}
