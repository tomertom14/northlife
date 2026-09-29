using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data.Configurations;

public sealed class AutoModerationSettingsConfiguration : IEntityTypeConfiguration<AutoModerationSettings>
{
    public void Configure(EntityTypeBuilder<AutoModerationSettings> builder)
    {
        builder.ToTable("auto_moderation_settings", table =>
        {
            table.HasCheckConstraint("ck_auto_moderation_settings_mode", "mode IN ('Off', 'NotesOnly', 'Approve')");
            table.HasCheckConstraint("ck_auto_moderation_settings_single_row", $"id = '{AutoModerationSettings.SingletonId}'");
        });
        builder.HasKey(settings => settings.Id).HasName("pk_auto_moderation_settings");
        builder.Property(settings => settings.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(settings => settings.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20);
        builder.Property(settings => settings.RunAtLocal).HasColumnName("run_at_local");
        builder.Property(settings => settings.MinAccountAgeDays).HasColumnName("min_account_age_days");
        builder.Property(settings => settings.MinApprovedEvents).HasColumnName("min_approved_events");
        builder.Property(settings => settings.RejectionLookbackDays).HasColumnName("rejection_lookback_days");
        builder.Property(settings => settings.MaxAutoApprovalsPerOwnerPerDay).HasColumnName("max_auto_approvals_per_owner_per_day");
        builder.Property(settings => settings.MaxDaysAhead).HasColumnName("max_days_ahead");
        builder.Property(settings => settings.MaxDurationDays).HasColumnName("max_duration_days");
        builder.Property(settings => settings.MaxPrice).HasColumnName("max_price").HasPrecision(10, 2);
        builder.Property(settings => settings.DuplicateSimilarity).HasColumnName("duplicate_similarity");
        builder.Property(settings => settings.DuplicateDistanceMeters).HasColumnName("duplicate_distance_meters");
        builder.Property(settings => settings.BannedWords).HasColumnName("banned_words").HasColumnType("text[]");
        builder.Property(settings => settings.LastScheduledRunDate).HasColumnName("last_scheduled_run_date");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");

        // The row always exists; the service and the admin page only ever update it.
        builder.HasData(new AutoModerationSettings
        {
            Id = AutoModerationSettings.SingletonId,
            UpdatedAtUtc = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
        });
    }
}

public sealed class AutoModerationRunConfiguration : IEntityTypeConfiguration<AutoModerationRun>
{
    public void Configure(EntityTypeBuilder<AutoModerationRun> builder)
    {
        builder.ToTable("auto_moderation_runs", table =>
        {
            table.HasCheckConstraint("ck_auto_moderation_runs_trigger", "trigger IN ('Scheduled', 'Manual')");
            table.HasCheckConstraint("ck_auto_moderation_runs_mode", "mode IN ('Off', 'NotesOnly', 'Approve')");
        });
        builder.HasKey(run => run.Id).HasName("pk_auto_moderation_runs");
        builder.Property(run => run.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(run => run.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.TriggeredById).HasColumnName("triggered_by_id");
        builder.Property(run => run.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamptz");
        builder.Property(run => run.FinishedAtUtc).HasColumnName("finished_at_utc").HasColumnType("timestamptz");
        builder.Property(run => run.Checked).HasColumnName("checked");
        builder.Property(run => run.Approved).HasColumnName("approved");
        builder.Property(run => run.WouldApprove).HasColumnName("would_approve");
        builder.Property(run => run.Held).HasColumnName("held");
        builder.Property(run => run.Error).HasColumnName("error").HasMaxLength(2000);
        builder.HasIndex(run => run.StartedAtUtc).IsDescending().HasDatabaseName("ix_auto_moderation_runs_started");
        builder.HasIndex(run => run.TriggeredById).HasDatabaseName("ix_auto_moderation_runs_triggered_by_id");
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(run => run.TriggeredById)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_auto_moderation_runs_users_triggered_by_id");
    }
}

public sealed class AutoModerationDecisionConfiguration : IEntityTypeConfiguration<AutoModerationDecision>
{
    public void Configure(EntityTypeBuilder<AutoModerationDecision> builder)
    {
        builder.ToTable("auto_moderation_decisions", table =>
            table.HasCheckConstraint("ck_auto_moderation_decisions_outcome", "outcome IN ('Approved', 'WouldApprove', 'Held')"));
        builder.HasKey(decision => decision.Id).HasName("pk_auto_moderation_decisions");
        builder.Property(decision => decision.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(decision => decision.RunId).HasColumnName("run_id");
        builder.Property(decision => decision.EventId).HasColumnName("event_id");
        builder.Property(decision => decision.EventRevision).HasColumnName("event_revision");
        builder.Property(decision => decision.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(20);
        builder.Property(decision => decision.Reasons).HasColumnName("reasons").HasColumnType("jsonb");
        builder.Property(decision => decision.DecidedAtUtc).HasColumnName("decided_at_utc").HasColumnType("timestamptz");
        builder.HasIndex(decision => new { decision.EventId, decision.DecidedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_auto_moderation_decisions_event_decided");
        builder.HasIndex(decision => decision.RunId).HasDatabaseName("ix_auto_moderation_decisions_run");
        builder.HasOne<AutoModerationRun>()
            .WithMany()
            .HasForeignKey(decision => decision.RunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_auto_moderation_decisions_runs_run_id");
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(decision => decision.EventId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_auto_moderation_decisions_events_event_id");
    }
}
