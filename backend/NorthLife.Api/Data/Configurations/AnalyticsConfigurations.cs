using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

// The raw "interactions" table is range-partitioned by month, which EF Core cannot model. The
// migration creates it with SQL and the analytics services read and write it with SQL.

public sealed class EventStatsHourlyConfiguration : IEntityTypeConfiguration<EventStatsHourly>
{
    public void Configure(EntityTypeBuilder<EventStatsHourly> builder)
    {
        builder.ToTable("event_stats_hourly");
        builder.HasKey(stats => new { stats.EventId, stats.HourUtc }).HasName("pk_event_stats_hourly");
        builder.Property(stats => stats.EventId).HasColumnName("event_id");
        builder.Property(stats => stats.HourUtc).HasColumnName("hour_utc").HasColumnType("timestamptz");
        builder.Property(stats => stats.Impressions).HasColumnName("impressions");
        builder.Property(stats => stats.DetailViews).HasColumnName("detail_views");
        builder.Property(stats => stats.Navigations).HasColumnName("navigations");
        builder.Property(stats => stats.Shares).HasColumnName("shares");
        builder.HasIndex(stats => stats.HourUtc).HasDatabaseName("ix_event_stats_hourly_hour");
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(stats => stats.EventId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_event_stats_hourly_events_event_id");
    }
}

public sealed class EventStatsDailyConfiguration : IEntityTypeConfiguration<EventStatsDaily>
{
    public void Configure(EntityTypeBuilder<EventStatsDaily> builder)
    {
        builder.ToTable("event_stats_daily");
        builder.HasKey(stats => new { stats.EventId, stats.Day }).HasName("pk_event_stats_daily");
        builder.Property(stats => stats.EventId).HasColumnName("event_id");
        builder.Property(stats => stats.Day).HasColumnName("day");
        builder.Property(stats => stats.Impressions).HasColumnName("impressions");
        builder.Property(stats => stats.DetailViews).HasColumnName("detail_views");
        builder.Property(stats => stats.Navigations).HasColumnName("navigations");
        builder.Property(stats => stats.Shares).HasColumnName("shares");
        builder.Property(stats => stats.Visitors).HasColumnName("visitors");
        builder.Property(stats => stats.VisitorSketch).HasColumnName("visitor_sketch");
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(stats => stats.EventId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_event_stats_daily_events_event_id");
    }
}

public sealed class EventPopularityConfiguration : IEntityTypeConfiguration<EventPopularity>
{
    public void Configure(EntityTypeBuilder<EventPopularity> builder)
    {
        builder.ToTable("event_popularity");
        builder.HasKey(popularity => popularity.EventId).HasName("pk_event_popularity");
        builder.Property(popularity => popularity.EventId).HasColumnName("event_id");
        builder.Property(popularity => popularity.LogScore).HasColumnName("log_score");
        builder.Property(popularity => popularity.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        builder.HasIndex(popularity => popularity.LogScore).IsDescending().HasDatabaseName("ix_event_popularity_log_score");
        builder.HasOne<Event>()
            .WithOne()
            .HasForeignKey<EventPopularity>(popularity => popularity.EventId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_event_popularity_events_event_id");
    }
}

public sealed class PositionPropensityConfiguration : IEntityTypeConfiguration<PositionPropensityRow>
{
    public void Configure(EntityTypeBuilder<PositionPropensityRow> builder)
    {
        builder.ToTable("position_propensities");
        builder.HasKey(row => new { row.Surface, row.Position }).HasName("pk_position_propensities");
        builder.Property(row => row.Surface).HasColumnName("surface");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Propensity).HasColumnName("propensity");
        builder.Property(row => row.RawPropensity).HasColumnName("raw_propensity");
        builder.Property(row => row.NaiveRatio).HasColumnName("naive_ratio");
        builder.Property(row => row.Impressions).HasColumnName("impressions");
        builder.Property(row => row.Clicks).HasColumnName("clicks");
        builder.Property(row => row.EstimatedAtUtc).HasColumnName("estimated_at_utc").HasColumnType("timestamptz");
    }
}

public sealed class EventSimilarityConfiguration : IEntityTypeConfiguration<EventSimilarity>
{
    public void Configure(EntityTypeBuilder<EventSimilarity> builder)
    {
        builder.ToTable("event_similarities");
        builder.HasKey(row => new { row.SourceEventId, row.TargetEventId }).HasName("pk_event_similarities");
        builder.Property(row => row.SourceEventId).HasColumnName("source_event_id");
        builder.Property(row => row.TargetEventId).HasColumnName("target_event_id");
        builder.Property(row => row.Rank).HasColumnName("rank");
        builder.Property(row => row.Content).HasColumnName("content");
        builder.Property(row => row.Collaborative).HasColumnName("collaborative");
        builder.Property(row => row.Blended).HasColumnName("blended");
        builder.Property(row => row.CoVisitors).HasColumnName("co_visitors");
        builder.Property(row => row.ComputedAtUtc).HasColumnName("computed_at_utc").HasColumnType("timestamptz");
        builder.HasIndex(row => new { row.SourceEventId, row.Rank }).HasDatabaseName("ix_event_similarities_source_rank");
        builder.HasIndex(row => row.TargetEventId).HasDatabaseName("ix_event_similarities_target");
        builder.HasOne<Event>().WithMany().HasForeignKey(row => row.SourceEventId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_event_similarities_events_source_event_id");
        builder.HasOne<Event>().WithMany().HasForeignKey(row => row.TargetEventId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_event_similarities_events_target_event_id");
    }
}

public sealed class AnalyticsCheckpointConfiguration : IEntityTypeConfiguration<AnalyticsCheckpoint>
{
    public void Configure(EntityTypeBuilder<AnalyticsCheckpoint> builder)
    {
        builder.ToTable("analytics_checkpoints");
        builder.HasKey(checkpoint => checkpoint.Name).HasName("pk_analytics_checkpoints");
        builder.Property(checkpoint => checkpoint.Name).HasColumnName("name").HasMaxLength(40);
        builder.Property(checkpoint => checkpoint.ProcessedUntilUtc).HasColumnName("processed_until_utc").HasColumnType("timestamptz");
    }
}
