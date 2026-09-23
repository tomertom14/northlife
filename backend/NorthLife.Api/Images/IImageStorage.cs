namespace NorthLife.Api.Images;

public interface IImageStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
    bool Exists(string storageKey);
}
