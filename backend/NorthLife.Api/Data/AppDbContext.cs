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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
