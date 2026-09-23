namespace NorthLife.Api.Images;

public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";
    public string RootPath { get; init; } = ".data/images";
}
