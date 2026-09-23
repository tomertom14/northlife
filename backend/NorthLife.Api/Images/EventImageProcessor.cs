using ImageMagick;
using Microsoft.AspNetCore.Http;

namespace NorthLife.Api.Images;

public sealed class EventImageProcessor
{
    public const long MaximumUploadBytes = 5 * 1024 * 1024;
    public const uint MaximumDimension = 12_000;
    public const ulong MaximumPixels = 40_000_000;
    public const uint OutputLongestEdge = 1_600;

    private static readonly IReadOnlyDictionary<string, MagickFormat> AllowedTypes =
        new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = MagickFormat.Jpeg,
            ["image/png"] = MagickFormat.Png,
            ["image/webp"] = MagickFormat.WebP,
        };

    public async Task<ProcessedImage> ProcessAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumUploadBytes)
        {
            throw new ImageValidationException(
                "file",
                $"Image size must be between 1 byte and {MaximumUploadBytes} bytes.");
        }

        if (!AllowedTypes.TryGetValue(file.ContentType, out var declaredFormat))
        {
            throw new ImageValidationException(
                "file",
                "Only JPEG, PNG, and WebP images are accepted.");
        }

        await using var input = new MemoryStream((int)file.Length);
        await file.CopyToAsync(input, cancellationToken);
        if (input.Length > MaximumUploadBytes)
        {
            throw new ImageValidationException("file", "Image exceeds the 5 MB limit.");
        }

        input.Position = 0;
        MagickImageInfo info;
        try
        {
            info = new MagickImageInfo(input);
        }
        catch (MagickException)
        {
            throw new ImageValidationException("file", "The uploaded file is not a valid image.");
        }

        if (info.Format != declaredFormat)
        {
            throw new ImageValidationException(
                "file",
                "The file content does not match its declared image type.");
        }

        if (info.Width is 0 || info.Height is 0 ||
            info.Width > MaximumDimension ||
            info.Height > MaximumDimension ||
            (ulong)info.Width * info.Height > MaximumPixels)
        {
            throw new ImageValidationException(
                "file",
                "Image dimensions are invalid or exceed the safe processing limit.");
        }

        input.Position = 0;
        await using var output = new MemoryStream();
        try
        {
            using var image = new MagickImage(input);
            image.AutoOrient();
            image.Strip();
            image.Resize(new MagickGeometry(OutputLongestEdge, OutputLongestEdge)
            {
                Greater = true,
            });
            image.Format = MagickFormat.WebP;
            image.Quality = 82;
            await image.WriteAsync(output, cancellationToken);

            return new ProcessedImage(
                output.ToArray(),
                image.Width,
                image.Height);
        }
        catch (MagickException)
        {
            throw new ImageValidationException("file", "The image could not be safely processed.");
        }
    }
}

public sealed record ProcessedImage(byte[] Bytes, uint Width, uint Height);
