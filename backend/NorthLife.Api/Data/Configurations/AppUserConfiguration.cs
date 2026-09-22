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

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");
    }
}
