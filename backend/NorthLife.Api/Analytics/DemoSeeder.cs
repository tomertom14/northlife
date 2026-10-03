using System.Security.Cryptography;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Images;
using NorthLife.Api.Models;
using Npgsql;
using NpgsqlTypes;

namespace NorthLife.Api.Analytics;

public sealed record DemoSeedResult(int Owners, int Events, int Interactions, int Visitors, string OwnerPassword);

/// <summary>
/// "--seed-demo": the demo catalogue plus 30 days of simulated traffic, for local testing and the
/// ranking and recommendation experiments. Runs once; later runs see the demo owners and stop.
/// </summary>
public sealed class DemoSeeder(
    AppDbContext dbContext,
    IImageStorage storage,
    AnalyticsRollupService rollup,
    Ranking.PositionBiasService positionBias,
    IPasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public async Task<DemoSeedResult?> SeedAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(user => user.Email.EndsWith(DemoCatalog.EmailDomain), cancellationToken)) return null;

        var now = timeProvider.GetUtcNow();
        var password = configuration["Demo:OwnerPassword"];
        if (string.IsNullOrWhiteSpace(password)) password = "Demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

        var events = await CreateCatalogAsync(now, password, cancellationToken);
        var interactions = await SimulateTrafficAsync(now, cancellationToken);
        return new DemoSeedResult(DemoCatalog.Owners.Length, events, interactions.Count, interactions.Select(row => row.VisitorId).Distinct().Count(), password);
    }

    private async Task<int> CreateCatalogAsync(DateTimeOffset now, string password, CancellationToken cancellationToken)
    {
        var random = new Random(2026);
        var owners = new Dictionary<EventCategory, AppUser>();
        for (var index = 0; index < DemoCatalog.Owners.Length; index++)
        {
            var (fullName, businessName, categories) = DemoCatalog.Owners[index];
            var owner = new AppUser
            {
                FullName = fullName,
                Email = $"owner{index + 1}{DemoCatalog.EmailDomain}",
                NormalizedEmail = $"OWNER{index + 1}{DemoCatalog.EmailDomain}".ToUpperInvariant(),
                PasswordHash = string.Empty,
                Phone = "0500000000",
                BusinessName = businessName,
                Role = UserRole.BusinessOwner,
                CreatedAtUtc = now.AddDays(-45),
                EmailConfirmedAtUtc = now.AddDays(-45),
            };
            owner.PasswordHash = passwordHasher.HashPassword(owner, password);
            dbContext.Users.Add(owner);
            foreach (var category in categories) owners[category] = owner;
        }

        var images = new Dictionary<EventCategory, EventImage>();
        foreach (var (category, color) in DemoCatalog.CategoryColors)
        {
            var key = $"demo/{category.ToString().ToLowerInvariant()}.webp";
            var bytes = RenderCover(color);
            // Covers are deterministic, so a file left from an earlier seed is the same picture.
            if (!storage.Exists(key)) await storage.SaveAsync(key, new MemoryStream(bytes), cancellationToken);
            var image = new EventImage
            {
                UploaderId = owners.TryGetValue(category, out var owner) ? owner.Id : owners.Values.First().Id,
                StorageKey = key,
                ContentType = "image/webp",
                SizeBytes = bytes.Length,
                CreatedAtUtc = now.AddDays(-40),
            };
            images[category] = image;
            dbContext.EventImages.Add(image);
        }

        var today = JerusalemDays.Of(now);
        var firstOfCategory = new HashSet<EventCategory>();
        var highlighted = new HashSet<EventCategory> { EventCategory.Music, EventCategory.Nightlife, EventCategory.Food, EventCategory.Workshops, EventCategory.Outdoors, EventCategory.Culture };
        var created = 0;
        foreach (var spec in DemoCatalog.Events)
        {
            var locality = DemoCatalog.Localities[random.Next(DemoCatalog.Localities.Length)];
            var hours = DemoCatalog.StartHours(spec.Category);
            // The first event of each category is today, so the "today" feed has something in every category.
            var dayOffset = firstOfCategory.Add(spec.Category) ? 0 : random.Next(0, 14);
            var start = JerusalemDays.StartUtc(today.AddDays(dayOffset)).AddHours(hours[random.Next(hours.Length)]).AddMinutes(random.Next(0, 2) * 30);
            var duration = TimeSpan.FromHours(DemoCatalog.DurationHours(spec.Category, random));
            if (start + duration <= now) start = start.AddDays(1);
            var price = DemoCatalog.Price(spec.Category, random);
            var venue = DemoCatalog.Venue(spec.Category, random);
            var owner = owners[spec.Category];
            var isHighlighted = highlighted.Remove(spec.Category);

            dbContext.Events.Add(new Event
            {
                OwnerId = owner.Id,
                ImageId = images[spec.Category].Id,
                Title = spec.Title,
                Description = DemoCatalog.Description(spec, venue, locality, price),
                Category = spec.Category,
                VenueName = venue,
                Locality = locality.Name,
                Address = $"{venue}, {locality.Name}",
                Latitude = locality.Latitude + (decimal)((random.NextDouble() - 0.5) * 0.01),
                Longitude = locality.Longitude + (decimal)((random.NextDouble() - 0.5) * 0.01),
                StartAtUtc = start,
                EndAtUtc = start + duration,
                Price = price,
                OrganizerName = owner.BusinessName,
                Tags = spec.Tags,
                Status = EventStatus.Published,
                IsHighlighted = isHighlighted,
                CreatedAtUtc = now.AddDays(-35),
                UpdatedAtUtc = now.AddDays(-35),
            });
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<IReadOnlyList<RawInteraction>> SimulateTrafficAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Stay behind the rollup checkpoint: the worker only reads rows at or after it, so these rows
        // are counted once, here, and never again.
        var checkpoint = await dbContext.AnalyticsCheckpoints.SingleAsync(candidate => candidate.Name == AnalyticsRollupService.CheckpointName, cancellationToken);
        var until = (checkpoint.ProcessedUntilUtc < now ? checkpoint.ProcessedUntilUtc : now).AddMinutes(-1);

        var events = await dbContext.Events.AsNoTracking()
            .Where(item => item.Status == EventStatus.Published && item.DeletedAtUtc == null && item.EndAtUtc > now)
            .Select(item => new SimulatedEvent(item.Id, item.Category, (double)item.Latitude, (double)item.Longitude, item.Price, item.StartAtUtc, item.IsHighlighted))
            .ToListAsync(cancellationToken);
        var homes = DemoCatalog.Localities.Select(locality => ((double)locality.Latitude, (double)locality.Longitude)).ToList();
        var traffic = SyntheticTraffic.Generate(events, homes, until, new SyntheticTrafficOptions { Visitors = 1500 });
        var rows = traffic.Interactions;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var month in rows.Select(row => new DateOnly(row.OccurredAtUtc.Year, row.OccurredAtUtc.Month, 1)).Distinct())
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT analytics_create_interaction_partition({month})", cancellationToken);
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await using (var writer = await connection.BeginBinaryImportAsync(
"COPY interactions (event_id, visitor_id, type, source, position, occurred_at_utc, context_key) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.EventId, NpgsqlDbType.Uuid, cancellationToken);
                await writer.WriteAsync(row.VisitorId, NpgsqlDbType.Uuid, cancellationToken);
                await writer.WriteAsync((short)row.Type, NpgsqlDbType.Smallint, cancellationToken);
                await writer.WriteAsync((short)row.Source, NpgsqlDbType.Smallint, cancellationToken);
                if (row.Position is { } position) await writer.WriteAsync(position, NpgsqlDbType.Smallint, cancellationToken);
                else await writer.WriteNullAsync(cancellationToken);
                await writer.WriteAsync(row.OccurredAtUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                if (row.ContextKey is { } context) await writer.WriteAsync(context, NpgsqlDbType.Integer, cancellationToken);
                else await writer.WriteNullAsync(cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // Fit the position bias on the simulated month first, so popularity credits clicks fairly.
        var propensities = await positionBias.EstimateAsync(cancellationToken);
        rollup.Weight = Ranking.PopularityWeights.With(new Ranking.PropensityTable(propensities.ToDictionary(row => row.Position, row => row.Propensity)));
        await rollup.ApplyBatchAsync(RollupAggregator.Aggregate(rows, rollup.Weight), until, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// "--seed-load N": N extra published events over the next 30 days for performance tests, owned by
    /// load@demo.northlife.local (removed by scripts/reset-demo-data.sql with the rest of the demo).
    /// </summary>
    public async Task<int> SeedLoadAsync(int count, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var email = "load" + DemoCatalog.EmailDomain;
        var owner = await dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (owner is null)
        {
            owner = new AppUser
            {
                FullName = "בדיקת עומסים",
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Phone = "0500000000",
                BusinessName = "בדיקת עומסים",
                Role = UserRole.BusinessOwner,
                CreatedAtUtc = now,
                EmailConfirmedAtUtc = now,
            };
            dbContext.Users.Add(owner);
        }

        const string key = "demo/load.webp";
        var cover = RenderCover(DemoCatalog.CategoryColors[EventCategory.Other]);
        if (!storage.Exists(key)) await storage.SaveAsync(key, new MemoryStream(cover), cancellationToken);
        var image = new EventImage { UploaderId = owner.Id, StorageKey = key, ContentType = "image/webp", SizeBytes = cover.Length, CreatedAtUtc = now };
        dbContext.EventImages.Add(image);
        await dbContext.SaveChangesAsync(cancellationToken);

        var random = new Random(77);
        var today = JerusalemDays.Of(now);
        for (var created = 0; created < count;)
        {
            for (var index = 0; index < 1000 && created < count; index++, created++)
            {
                var spec = DemoCatalog.Events[random.Next(DemoCatalog.Events.Length)];
                var locality = DemoCatalog.Localities[random.Next(DemoCatalog.Localities.Length)];
                var hours = DemoCatalog.StartHours(spec.Category);
                var start = JerusalemDays.StartUtc(today.AddDays(random.Next(0, 30))).AddHours(hours[random.Next(hours.Length)]);
                if (start <= now) start = start.AddDays(1);
                var price = DemoCatalog.Price(spec.Category, random);
                var venue = DemoCatalog.Venue(spec.Category, random);
                dbContext.Events.Add(new Event
                {
                    OwnerId = owner.Id,
                    ImageId = image.Id,
                    Title = $"{spec.Title} #{created + 1}",
                    Description = DemoCatalog.Description(spec, venue, locality, price),
                    Category = spec.Category,
                    VenueName = venue,
                    Locality = locality.Name,
                    Address = $"{venue}, {locality.Name}",
                    Latitude = locality.Latitude + (decimal)((random.NextDouble() - 0.5) * 0.2),
                    Longitude = locality.Longitude + (decimal)((random.NextDouble() - 0.5) * 0.2),
                    StartAtUtc = start,
                    EndAtUtc = start.AddHours(DemoCatalog.DurationHours(spec.Category, random)),
                    Price = price,
                    OrganizerName = owner.BusinessName,
                    Tags = spec.Tags,
                    Status = EventStatus.Published,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        return count;
    }

    /// <summary>A soft two-tone cover in the category colour, so demo cards look like real ones.</summary>
    private static byte[] RenderCover(string color)
    {
        var light = new MagickColor(color);
        var dark = new MagickColor((byte)(light.R * 0.45), (byte)(light.G * 0.45), (byte)(light.B * 0.45));
        using var image = new MagickImage($"gradient:{light.ToHexString()}-{dark.ToHexString()}", new MagickReadSettings { Width = 1200, Height = 800 });
        new Drawables()
            .FillColor(new MagickColor(255, 255, 255, 46))
            .Circle(880, 250, 880, 400)
            .FillColor(new MagickColor(0, 0, 0, 40))
            .Polygon(new PointD(0, 800), new PointD(0, 560), new PointD(300, 470), new PointD(620, 590), new PointD(940, 480), new PointD(1200, 560), new PointD(1200, 800))
            .Draw(image);
        image.Format = MagickFormat.WebP;
        image.Quality = 80;
        return image.ToByteArray();
    }
}
