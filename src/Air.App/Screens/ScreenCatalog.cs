namespace Platform.App.Screens;

public sealed class ScreenCatalog(IServiceProvider services, ModuleCatalog modules)
{
  public async Task<ScreenDto> ResolveScreenAsync(string module, string feature, CancellationToken ct = default)
  {
    var path = $"{module.ToLowerInvariant()}/{feature.ToLowerInvariant()}";
    var feat = modules.FindFeature(path)
      ?? throw new KeyNotFoundException($"Feature '{path}' not found in registered modules.");

    if (feat.ServiceType is null)
    {
      throw new InvalidOperationException($"Feature '{path}' does not declare a screen service.");
    }

    var service = services.GetRequiredService(feat.ServiceType);
    if (service is IGridService gridService)
    {
      return await gridService.BuildScreenAsync(path, ct);
    }

    if (service is IFormService formService)
    {
      return await formService.BuildScreenAsync(path, ct);
    }

    if (service is IScreenService screenService)
    {
      return await screenService.BuildScreenAsync(path, ct);
    }

    throw new InvalidOperationException($"Service '{feat.ServiceType.Name}' does not implement a known screen interface.");
  }
}
