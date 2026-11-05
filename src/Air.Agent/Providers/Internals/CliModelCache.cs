namespace Platform.Agent.Providers.Internals;

internal sealed class CliModelCache
{
  private string? _cachedKey;
  private readonly SemaphoreSlim _lock = new(1, 1);

  public IReadOnlyList<AgentModelDto>? CachedModels { get; private set; }

  public async Task<IReadOnlyList<AgentModelDto>> GetOrLoadAsync(
    string? cacheKey,
    bool forceRefresh,
    Func<CancellationToken, Task<List<AgentModelDto>>> loader,
    CancellationToken ct)
  {
    if (cacheKey is null)
    {
      return [];
    }

    if (!forceRefresh &&
        CachedModels is { Count: > 0 } &&
        string.Equals(_cachedKey, cacheKey, StringComparison.Ordinal))
    {
      return CachedModels;
    }

    await _lock.WaitAsync(ct);
    try
    {
      if (!forceRefresh &&
          CachedModels is { Count: > 0 } &&
          string.Equals(_cachedKey, cacheKey, StringComparison.Ordinal))
      {
        return CachedModels;
      }

      var discovered = await loader(ct);
      if (discovered.Count > 0)
      {
        CachedModels = discovered;
        _cachedKey = cacheKey;
        return CachedModels;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Ignore model discovery errors and fall back to cached or empty list
    }
    finally
    {
      _lock.Release();
    }

    return CachedModels ?? [];
  }
}
