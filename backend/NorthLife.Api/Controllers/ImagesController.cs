using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Images;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
public sealed class ImagesController(
    AppDbContext dbContext,
    EventImageService imageService,
    IImageStorage storage,
    TimeProvider timeProvider,
    ILogger<ImagesController> logger) : ControllerBase
{
    [HttpPost("api/manage/images")]
    [Authorize(Roles = $"{nameof(UserRole.BusinessOwner)},{nameof(UserRole.Admin)}")]
    [RequestFormLimits(MultipartBodyLengthLimit = EventImageProcessor.MaximumUploadBytes + 64 * 1024)]
    [RequestSizeLimit(EventImageProcessor.MaximumUploadBytes + 64 * 1024)]
    public async Task<ActionResult<ImageUploadResponse>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return StatusCode(
                StatusCodes.Status201Created,
                await imageService.UploadAsync(file, userId, cancellationToken));
        }
        catch (ImageValidationException exception)
        {
            var details = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [exception.Field] = [exception.Message],
            })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The image upload is invalid.",
            };
            details.Extensions["code"] = "invalid_image";
            return BadRequest(details);
        }
    }

    [HttpGet("api/images/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var image = await dbContext.EventImages
            .AsNoTracking()
            .Include(candidate => candidate.Events)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (image is null)
        {
            return NotFound();
        }

        var isPublic = image.Events.Any(eventItem =>
            eventItem.Status == EventStatus.Published &&
            eventItem.EndAtUtc > now);
        var isOwner = TryUserId(out var userId) && image.UploaderId == userId;
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));

        if (!isPublic && !isOwner && !isAdmin)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogError(
                "Image {ImageId} metadata exists but storage object is missing.",
                image.Id);
            return NotFound();
        }

        Response.Headers.CacheControl = isPublic
            ? "public,max-age=86400,immutable"
            : "private,no-store";
        return File(stream, image.ContentType, enableRangeProcessing: true);
    }

    private bool TryUserId(out Guid userId) =>
        Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);
}
