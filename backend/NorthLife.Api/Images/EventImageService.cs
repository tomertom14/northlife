using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Images;

public sealed class EventImageService(
    AppDbContext dbContext,
    IImageStorage storage,
    EventImageProcessor processor,
    TimeProvider timeProvider)
{
    public async Task<ImageUploadResponse> UploadAsync(
        IFormFile file,
        Guid uploaderId,
        CancellationToken cancellationToken)
    {
        var processed = await processor.ProcessAsync(file, cancellationToken);
        await using var content = new MemoryStream(processed.Bytes, writable: false);

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var storageKey = $"{now:yyyy/MM}/{id:N}.webp";
        await storage.SaveAsync(storageKey, content, cancellationToken);

        var entity = new EventImage
        {
            Id = id,
            UploaderId = uploaderId,
            StorageKey = storageKey,
            ContentType = "image/webp",
            SizeBytes = processed.Bytes.LongLength,
            CreatedAtUtc = now,
        };
        dbContext.EventImages.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }

        return new ImageUploadResponse(
            entity.Id,
            $"/api/images/{entity.Id}",
            entity.ContentType,
            entity.SizeBytes,
            processed.Width,
            processed.Height);
    }

    public async Task<EventImage> RequireOwnedAsync(
        Guid imageId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var image = await dbContext.EventImages.SingleOrDefaultAsync(
            candidate => candidate.Id == imageId,
            cancellationToken);
        if (image is null || !EventImageAccessPolicy.CanManage(image, userId, isAdmin))
        {
            throw new ImageNotFoundException();
        }

        return image;
    }

    public async Task<int> CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddHours(-24);
        // Soft-deleted events still hold the foreign key, so they count as references here.
        var candidates = await dbContext.EventImages
            .IgnoreQueryFilters()
            .Where(image =>
                image.CreatedAtUtc <= cutoff &&
                !image.Events.Any())
            .ToListAsync(cancellationToken);

        dbContext.EventImages.RemoveRange(candidates);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Remove files only once the rows are gone, so a failed save never orphans metadata.
        foreach (var image in candidates)
        {
            await storage.DeleteAsync(image.StorageKey, cancellationToken);
        }

        return candidates.Count;
    }
}

public sealed class ImageValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class ImageNotFoundException : Exception;
