namespace Platform.Api.Files;

public sealed class DefaultFileStores(IFileStorage storage) : IFileStores
{
  public IFileStorage Of(string? store = null) => storage;
}
