using NorthLife.Api.Images;
using NorthLife.Api.Models;

namespace NorthLife.Tests;

public sealed class EventImageAccessPolicyTests
{
    private static readonly Guid OwnerId =
        Guid.Parse("019924c0-0000-7000-8000-000000000200");

    private static readonly EventImage Image = new()
    {
        Id = Guid.Parse("019924c0-0000-7000-9000-000000000200"),
        UploaderId = OwnerId,
        StorageKey = "private/image.webp",
        ContentType = "image/webp",
        SizeBytes = 100,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Owner_can_manage_image()
    {
        Assert.True(EventImageAccessPolicy.CanManage(Image, OwnerId, isAdmin: false));
    }

    [Fact]
    public void Another_owner_cannot_manage_image()
    {
        Assert.False(EventImageAccessPolicy.CanManage(
            Image,
            Guid.Parse("019924c0-0000-7000-8000-000000000201"),
            isAdmin: false));
    }

    [Fact]
    public void Administrator_can_manage_image()
    {
        Assert.True(EventImageAccessPolicy.CanManage(
            Image,
            Guid.Empty,
            isAdmin: true));
    }
}
