using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("user_tokens", table =>
            table.HasCheckConstraint("ck_user_tokens_purpose", "purpose IN ('VerifyEmail', 'ResetPassword')"));

        builder.HasKey(token => token.Id).HasName("pk_user_tokens");
        builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(token => token.UserId).HasColumnName("user_id");
        builder.Property(token => token.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(30);
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        builder.Property(token => token.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(token => token.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamptz");
        builder.Property(token => token.UsedAtUtc).HasColumnName("used_at_utc").HasColumnType("timestamptz");

        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_user_tokens_token_hash");
        builder.HasIndex(token => new { token.UserId, token.Purpose }).HasDatabaseName("ix_user_tokens_user_purpose");

        builder.HasOne(token => token.User)
            .WithMany(user => user.Tokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_tokens_users_user_id");
    }
}

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_logins");
        builder.HasKey(login => new { login.Provider, login.Subject }).HasName("pk_external_logins");
        builder.Property(login => login.Provider).HasColumnName("provider").HasMaxLength(30);
        builder.Property(login => login.Subject).HasColumnName("subject").HasMaxLength(255);
        builder.Property(login => login.UserId).HasColumnName("user_id");
        builder.Property(login => login.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        builder.HasIndex(login => login.UserId).HasDatabaseName("ix_external_logins_user_id");
        builder.HasOne(login => login.User)
            .WithMany(user => user.ExternalLogins)
            .HasForeignKey(login => login.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_external_logins_users_user_id");
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id).HasName("pk_audit_entries");
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ActorId).HasColumnName("actor_id");
        builder.Property(entry => entry.Action).HasColumnName("action").HasMaxLength(60);
        builder.Property(entry => entry.TargetType).HasColumnName("target_type").HasMaxLength(30);
        builder.Property(entry => entry.TargetId).HasColumnName("target_id");
        builder.Property(entry => entry.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(entry => entry.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        builder.HasIndex(entry => new { entry.CreatedAtUtc, entry.Id }).HasDatabaseName("ix_audit_entries_created_id");
        builder.HasIndex(entry => entry.TargetId).HasDatabaseName("ix_audit_entries_target_id");
        builder.HasIndex(entry => entry.ActorId).HasDatabaseName("ix_audit_entries_actor_id");
        builder.HasOne(entry => entry.Actor)
            .WithMany()
            .HasForeignKey(entry => entry.ActorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_audit_entries_users_actor_id");
    }
}

public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("recovery_codes");
        builder.HasKey(code => code.Id).HasName("pk_recovery_codes");
        builder.Property(code => code.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(code => code.UserId).HasColumnName("user_id");
        builder.Property(code => code.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        builder.Property(code => code.UsedAtUtc).HasColumnName("used_at_utc").HasColumnType("timestamptz");

        builder.HasIndex(code => code.UserId).HasDatabaseName("ix_recovery_codes_user_id");
        builder.HasOne(code => code.User)
            .WithMany(user => user.RecoveryCodes)
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_recovery_codes_users_user_id");
    }
}
