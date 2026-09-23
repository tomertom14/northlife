using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Tests;

public sealed class OwnerEventInputValidatorTests
{
    [Fact]
    public void Valid_create_is_normalized()
    {
        var request = Request() with
        {
            Title = "  Event  ",
            Tags = [" music ", "Music", "north"],
        };

        var normalized = OwnerEventInputValidator.Validate(request, requireRevision: false);

        Assert.Equal("Event", normalized.Title);
        Assert.Equal(["music", "north"], normalized.Tags);
    }

    [Fact]
    public void Invalid_coordinates_price_and_time_are_rejected()
    {
        var valid = Request();
        var request = valid with
        {
            Latitude = 91,
            Longitude = -181,
            Price = -1,
            EndAt = valid.StartAt,
        };

        var exception = Assert.Throws<OwnerEventValidationException>(
            () => OwnerEventInputValidator.Validate(request, requireRevision: false));

        Assert.Contains("latitude", exception.Errors.Keys);
        Assert.Contains("longitude", exception.Errors.Keys);
        Assert.Contains("price", exception.Errors.Keys);
        Assert.Contains("endAt", exception.Errors.Keys);
    }

    [Fact]
    public void Edit_requires_revision()
    {
        var exception = Assert.Throws<OwnerEventValidationException>(
            () => OwnerEventInputValidator.Validate(
                Request() with { Revision = null },
                requireRevision: true));

        Assert.Contains("revision", exception.Errors.Keys);
    }

    private static OwnerEventUpsertRequest Request() =>
        new(
            "Event",
            "Description",
            EventCategory.Culture,
            "Venue",
            "Locality",
            "Address",
            33.2m,
            35.5m,
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            10,
            Guid.CreateVersion7(),
            "Organizer",
            ["tag"],
            null);
}
