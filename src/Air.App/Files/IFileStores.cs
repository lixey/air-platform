using Air.Data.Files;

namespace Air.App.Files;

public interface IFileStores
{
  IFileStorage Of(string? store = null);
}

public sealed class DefaultFileStores(IFileStorage storage) : IFileStores
{
  public IFileStorage Of(string? store = null) => storage;
}
