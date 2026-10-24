namespace Platform.App.Screens;

public interface IScreenService : IPlatformService
{
  Feature IPlatformService.Feature => Feature.FeaturesByServiceType.TryGetValue(GetType(), out var f)
    ? f
    : new Feature("", GetType().Name.Replace("Service", "").ToLowerInvariant(), GetType());
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
}
