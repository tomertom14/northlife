using ImageMagick;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NorthLife.Api.Images;

namespace NorthLife.Tests;

public sealed class EventImageProcessorTests
{
    [Fact]
    public async Task Valid_jpeg_is_resized_stripped_and_encoded_as_webp()
    {
        using var source = new MagickImage(MagickColors.Orange, 2400, 1200);
        source.Format = MagickFormat.Jpeg;
        source.Comment = "metadata that must not survive";
        await using var input = new MemoryStream();
        await source.WriteAsync(input);
        input.Position = 0;

        var result = await new EventImageProcessor().ProcessAsync(
            FormFile(input, "image/jpeg"),
            CancellationToken.None);

        using var processed = new MagickImage(result.Bytes);
        Assert.Equal(MagickFormat.WebP, processed.Format);
        Assert.Equal((uint)1600, processed.Width);
        Assert.Equal((uint)800, processed.Height);
        Assert.Null(processed.Comment);
    }

    [Fact]
    public async Task Spoofed_content_type_is_rejected()
    {
        using var source = new MagickImage(MagickColors.Green, 20, 20);
        source.Format = MagickFormat.Png;
        await using var input = new MemoryStream();
        await source.WriteAsync(input);
        input.Position = 0;

        var exception = await Assert.ThrowsAsync<ImageValidationException>(
            () => new EventImageProcessor().ProcessAsync(
                FormFile(input, "image/jpeg"),
                CancellationToken.None));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_decode()
    {
        await using var input = new MemoryStream(
            new byte[EventImageProcessor.MaximumUploadBytes + 1],
            writable: false);

        await Assert.ThrowsAsync<ImageValidationException>(
            () => new EventImageProcessor().ProcessAsync(
                FormFile(input, "image/png"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_image_signature_is_rejected()
    {
        await using var input = new MemoryStream([1, 2, 3, 4, 5], writable: false);

        await Assert.ThrowsAsync<ImageValidationException>(
            () => new EventImageProcessor().ProcessAsync(
                FormFile(input, "image/png"),
                CancellationToken.None));
    }

    private static FormFile FormFile(Stream stream, string contentType) =>
        new(stream, 0, stream.Length, "file", "untrusted-name")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
}

public sealed class LocalImageStorageTests
{
    [Fact]
    public async Task File_survives_storage_adapter_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"northlife-images-{Guid.NewGuid():N}");
        try
        {
            var options = Options.Create(new ImageStorageOptions { RootPath = root });
            var first = new LocalImageStorage(options, null!);
            await using var content = new MemoryStream([10, 20, 30], writable: false);
            await first.SaveAsync("2026/09/test.webp", content, CancellationToken.None);

            var restarted = new LocalImageStorage(options, null!);
            await using var stored = await restarted.OpenReadAsync(
                "2026/09/test.webp",
                CancellationToken.None);

            Assert.NotNull(stored);
            using var copy = new MemoryStream();
            await stored.CopyToAsync(copy);
            Assert.Equal(new byte[] { 10, 20, 30 }, copy.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Storage_key_cannot_escape_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"northlife-images-{Guid.NewGuid():N}");
        var storage = new LocalImageStorage(
            Options.Create(new ImageStorageOptions { RootPath = root }),
            null!);

        Assert.Throws<InvalidOperationException>(() => storage.Exists("../secret"));
    }
}
