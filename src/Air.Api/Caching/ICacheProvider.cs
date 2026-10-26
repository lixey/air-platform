namespace Platform.Api.Caching;

/// <summary>
/// Provider-agnostic cache abstraction backed by <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/>.
/// Combines fast L1 in-memory caching, stampede protection, tag-based eviction, and optional L2 distributed caching.
/// </summary>
public interface ICacheProvider
{
  ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

  ValueTask SetAsync<T>(
    string key,
    T value,
    TimeSpan? ttl = null,
    IEnumerable<string>? tags = null,
    CancellationToken ct = default);

  ValueTask<T?> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan? ttl = null,
    IEnumerable<string>? tags = null,
    CancellationToken ct = default);

  ValueTask RemoveAsync(string key, CancellationToken ct = default);

  ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}
