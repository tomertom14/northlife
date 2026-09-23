namespace NorthLife.Api.Contracts;

public sealed record ImageUploadResponse(
    Guid Id,
    string Url,
    string ContentType,
    long SizeBytes,
    uint Width,
    uint Height);
