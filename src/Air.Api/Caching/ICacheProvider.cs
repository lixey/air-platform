namespace Platform.Api.Caching;

/// <summary>
/// Provider-agnostic cache abstraction backed by <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
/// Uses zero-allocation in-memory object references when backed by the default in-memory cache,
/// and UTF-8 JSON serialization when backed by an external distributed cache (such as Redis).
/// </summary>
public interface ICacheProvider
{
  ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

  ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

  ValueTask<T?> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan? ttl = null,
    CancellationToken ct = default);

  ValueTask RemoveAsync(string key, CancellationToken ct = default);
}
