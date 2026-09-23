using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events", table =>
        {
            table.HasCheckConstraint("ck_events_price", "price >= 0");
            table.HasCheckConstraint("ck_events_time_range", "end_at_utc > start_at_utc");
            table.HasCheckConstraint("ck_events_latitude", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_events_longitude", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_events_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_events_status",
                "status IN ('Pending', 'Published', 'Rejected')");
            table.HasCheckConstraint(
                "ck_events_category",
                "category IN ('Music', 'Nightlife', 'Food', 'Workshops', 'Outdoors', 'Culture', 'Sports', 'Other')");
        });

        builder.HasKey(eventItem => eventItem.Id).HasName("pk_events");

        builder.Property(eventItem => eventItem.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(eventItem => eventItem.OwnerId).HasColumnName("owner_id");
        builder.Property(eventItem => eventItem.Title).HasColumnName("title").HasMaxLength(150);
        builder.Property(eventItem => eventItem.Description).HasColumnName("description").HasMaxLength(5000);
        builder.Property(eventItem => eventItem.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(30);
        builder.Property(eventItem => eventItem.VenueName).HasColumnName("venue_name").HasMaxLength(200);
        builder.Property(eventItem => eventItem.Locality).HasColumnName("locality").HasMaxLength(120);
        builder.Property(eventItem => eventItem.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(eventItem => eventItem.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(eventItem => eventItem.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(eventItem => eventItem.StartAtUtc).HasColumnName("start_at_utc").HasColumnType("timestamptz");
        builder.Property(eventItem => eventItem.EndAtUtc).HasColumnName("end_at_utc").HasColumnType("timestamptz");
        builder.Property(eventItem => eventItem.Price).HasColumnName("price").HasPrecision(10, 2);
        builder.Property(eventItem => eventItem.ImageId).HasColumnName("image_id");
        builder.Property(eventItem => eventItem.OrganizerName).HasColumnName("organizer_name").HasMaxLength(200);
        builder.Property(eventItem => eventItem.Tags).HasColumnName("tags").HasColumnType("text[]");
        builder.Property(eventItem => eventItem.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(eventItem => eventItem.IsHighlighted).HasColumnName("is_highlighted");
        builder.Property(eventItem => eventItem.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Property(eventItem => eventItem.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(eventItem => eventItem.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        builder.Property(eventItem => eventItem.DeletedAtUtc).HasColumnName("deleted_at_utc").HasColumnType("timestamptz");
        builder.Property(eventItem => eventItem.Revision)
            .HasColumnName("revision")
            .IsConcurrencyToken();

        builder.HasQueryFilter(eventItem => eventItem.DeletedAtUtc == null);

        builder.HasIndex(eventItem => new { eventItem.OwnerId, eventItem.UpdatedAtUtc })
            .HasDatabaseName("ix_events_owner_updated")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(eventItem => eventItem.ImageId)
            .HasDatabaseName("ix_events_image_id");
        builder.HasIndex(eventItem => new { eventItem.Status, eventItem.StartAtUtc, eventItem.Id })
            .HasDatabaseName("ix_events_status_start_id")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(eventItem => new { eventItem.Status, eventItem.Category, eventItem.StartAtUtc, eventItem.Id })
            .HasDatabaseName("ix_events_status_category_start_id")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(eventItem => new { eventItem.Status, eventItem.Locality, eventItem.StartAtUtc, eventItem.Id })
            .HasDatabaseName("ix_events_status_locality_start_id")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(eventItem => eventItem.EndAtUtc)
            .HasDatabaseName("ix_events_end_at")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(eventItem => new { eventItem.IsHighlighted, eventItem.StartAtUtc, eventItem.Id })
            .HasDatabaseName("ix_events_highlighted_start_id")
            .HasFilter("status = 'Published' AND deleted_at_utc IS NULL");

        builder.HasOne(eventItem => eventItem.Owner)
            .WithMany(user => user.Events)
            .HasForeignKey(eventItem => eventItem.OwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_events_users_owner_id");

        builder.HasOne(eventItem => eventItem.Image)
            .WithMany(image => image.Events)
            .HasForeignKey(eventItem => eventItem.ImageId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_events_event_images_image_id");
    }
}
