namespace Air.Data.Files;

public interface IFileStorage
{
  Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);
  Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
  Task DeleteAsync(string key, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<string>> KeysOlderThanAsync(DateTimeOffset moment, CancellationToken cancellationToken = default);
}
