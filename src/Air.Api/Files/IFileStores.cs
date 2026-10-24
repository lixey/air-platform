namespace Platform.Api.Files;

public interface IFileStores
{
  IFileStorage Of(string? store = null);
}
