using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventImage> EventImages => Set<EventImage>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<EventStatsHourly> EventStatsHourly => Set<EventStatsHourly>();
    public DbSet<EventStatsDaily> EventStatsDaily => Set<EventStatsDaily>();
    public DbSet<EventPopularity> EventPopularity => Set<EventPopularity>();
    public DbSet<AnalyticsCheckpoint> AnalyticsCheckpoints => Set<AnalyticsCheckpoint>();
    public DbSet<PositionPropensityRow> PositionPropensities => Set<PositionPropensityRow>();
    public DbSet<EventSimilarity> EventSimilarities => Set<EventSimilarity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateGeohashes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateGeohashes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Every save of an event keeps its geohash in step with its coordinates.</summary>
    private void UpdateGeohashes()
    {
        foreach (var entry in ChangeTracker.Entries<Event>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var hash = Ranking.Geohash.Encode((double)entry.Entity.Latitude, (double)entry.Entity.Longitude);
            if (entry.Entity.Geohash != hash) entry.Entity.Geohash = hash;
        }
    }
}
