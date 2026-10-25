namespace Platform.Data;

public interface IAgentModelSynchronizer
{
  Task<int> SyncFromProvidersAsync(
    PlatformDbContext db,
    CancellationToken ct = default,
    string? providerHandle = null);
}
