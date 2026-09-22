using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class EventImageConfiguration : IEntityTypeConfiguration<EventImage>
{
    public void Configure(EntityTypeBuilder<EventImage> builder)
    {
        builder.ToTable("event_images", table =>
        {
            table.HasCheckConstraint("ck_event_images_size", "size_bytes > 0");
        });

        builder.HasKey(image => image.Id).HasName("pk_event_images");

        builder.Property(image => image.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(image => image.UploaderId).HasColumnName("uploader_id");
        builder.Property(image => image.StorageKey).HasColumnName("storage_key").HasMaxLength(500);
        builder.Property(image => image.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(image => image.SizeBytes).HasColumnName("size_bytes");
        builder.Property(image => image.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        builder.HasIndex(image => image.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_event_images_storage_key");
        builder.HasIndex(image => image.UploaderId)
            .HasDatabaseName("ix_event_images_uploader_id");

        builder.HasOne(image => image.Uploader)
            .WithMany(user => user.UploadedImages)
            .HasForeignKey(image => image.UploaderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_event_images_users_uploader_id");
    }
}
