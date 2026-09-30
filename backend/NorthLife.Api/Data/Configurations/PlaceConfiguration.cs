using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("places", table =>
        {
            table.HasCheckConstraint("ck_places_latitude", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_places_longitude", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_places_revision", "revision >= 1");
            table.HasCheckConstraint("ck_places_status", "status IN ('Pending', 'Published', 'Rejected')");
            table.HasCheckConstraint(
                "ck_places_category",
                "category IN ('Food', 'Cafe', 'Nightlife', 'Classes', 'Sports', 'Culture', 'Outdoors', 'Services')");
        });

        builder.HasKey(place => place.Id).HasName("pk_places");

        builder.Property(place => place.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(place => place.OwnerId).HasColumnName("owner_id");
        builder.Property(place => place.Name).HasColumnName("name").HasMaxLength(150);
        builder.Property(place => place.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(30);
        builder.Property(place => place.Description).HasColumnName("description").HasMaxLength(3000);
        builder.Property(place => place.Locality).HasColumnName("locality").HasMaxLength(120);
        builder.Property(place => place.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(place => place.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(place => place.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(place => place.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(place => place.Website).HasColumnName("website").HasMaxLength(300);
        builder.Property(place => place.Instagram).HasColumnName("instagram").HasMaxLength(60);
        builder.Property(place => place.StudentPerk).HasColumnName("student_perk").HasMaxLength(200);
        builder.Property(place => place.ImageId).HasColumnName("image_id");
        builder.Property(place => place.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(place => place.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Property(place => place.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(place => place.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        builder.Property(place => place.DeletedAtUtc).HasColumnName("deleted_at_utc").HasColumnType("timestamptz");
        builder.Property(place => place.Revision).HasColumnName("revision").IsConcurrencyToken();
        // Byte-order collation, so a geohash prefix is a plain B-tree range scan (as for events).
        builder.Property(place => place.Geohash).HasColumnName("geohash").HasMaxLength(12).UseCollation("C");

        builder.HasQueryFilter(place => place.DeletedAtUtc == null);

        builder.HasIndex(place => place.Geohash)
            .HasDatabaseName("ix_places_geohash")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(place => new { place.Status, place.Category, place.Name })
            .HasDatabaseName("ix_places_status_category_name")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(place => new { place.Status, place.Locality, place.Name })
            .HasDatabaseName("ix_places_status_locality_name")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(place => new { place.OwnerId, place.UpdatedAtUtc })
            .HasDatabaseName("ix_places_owner_updated")
            .HasFilter("deleted_at_utc IS NULL");
        builder.HasIndex(place => place.ImageId).HasDatabaseName("ix_places_image_id");

        builder.HasOne(place => place.Owner)
            .WithMany(user => user.Places)
            .HasForeignKey(place => place.OwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_places_users_owner_id");

        builder.HasOne(place => place.Image)
            .WithMany(image => image.Places)
            .HasForeignKey(place => place.ImageId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_places_event_images_image_id");

        builder.HasMany(place => place.OpeningHours)
            .WithOne()
            .HasForeignKey(hours => hours.PlaceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_place_opening_hours_places_place_id");
    }
}

public sealed class PlaceOpeningHoursConfiguration : IEntityTypeConfiguration<PlaceOpeningHours>
{
    public void Configure(EntityTypeBuilder<PlaceOpeningHours> builder)
    {
        builder.ToTable("place_opening_hours", table =>
        {
            table.HasCheckConstraint("ck_place_opening_hours_day", "day_of_week BETWEEN 0 AND 6");
            table.HasCheckConstraint("ck_place_opening_hours_opens", "opens_minute BETWEEN 0 AND 1439");
            table.HasCheckConstraint("ck_place_opening_hours_closes", "closes_minute BETWEEN 0 AND 1439");
        });

        builder.HasKey(hours => new { hours.PlaceId, hours.DayOfWeek, hours.OpensMinute })
            .HasName("pk_place_opening_hours");

        builder.Property(hours => hours.PlaceId).HasColumnName("place_id");
        builder.Property(hours => hours.DayOfWeek).HasColumnName("day_of_week");
        builder.Property(hours => hours.OpensMinute).HasColumnName("opens_minute");
        builder.Property(hours => hours.ClosesMinute).HasColumnName("closes_minute");
    }
}
