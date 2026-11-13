namespace Platform.Api.Localization;

public interface ICultureCatalog
{
  IReadOnlyList<PlatformCulture> Cultures { get; }
  PlatformCulture Default { get; }
  PlatformCulture Resolve(string? token);
  PlatformCulture ResolveById(long? id);
  PlatformCulture ResolveRequest(HttpRequest request);
}
