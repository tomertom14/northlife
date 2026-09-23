namespace NorthLife.Api.Services;

public sealed record UtcEventWindow(DateTimeOffset Start, DateTimeOffset End);

public sealed class PublicEventQueryValidationException(
    string field,
    string message) : ArgumentException(message, field)
{
    public string Field { get; } = field;
}
