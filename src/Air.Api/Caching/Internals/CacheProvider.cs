using Microsoft.Extensions.Caching.Hybrid;

namespace Platform.Api.Caching.Internals;

internal sealed class CacheProvider(HybridCache cache) : ICacheProvider
{
  private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan MaxLocalExpiration = TimeSpan.FromSeconds(30);
  private static readonly HybridCacheEntryOptions ReadOnlyOptions = new()
  {
    Flags = HybridCacheEntryFlags.DisableUnderlyingData
  };

  public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return default;
    }

    return await cache.GetOrCreateAsync<T?>(
      key,
      static _ => ValueTask.FromResult<T?>(default),
      ReadOnlyOptions,
      cancellationToken: ct);
  }

  public async ValueTask SetAsync<T>(
    string key,
    T value,
    TimeSpan? ttl = null,
    IEnumerable<string>? tags = null,
    CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return;
    }

    await cache.SetAsync(key, value, CreateOptions(ttl), tags, ct);
  }

  public async ValueTask<T?> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan? ttl = null,
    IEnumerable<string>? tags = null,
    CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return await factory(ct);
    }

    var createdNull = false;
    var result = await cache.GetOrCreateAsync<T?>(
      key,
      async token =>
      {
        var value = await factory(token);
        if (value is null)
        {
          createdNull = true;
        }

        return value;
      },
      CreateOptions(ttl),
      tags,
      ct);

    if (createdNull && result is null)
    {
      await cache.RemoveAsync(key, ct);
    }

    return result;
  }

  public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
  {
    if (!string.IsNullOrEmpty(key))
    {
      await cache.RemoveAsync(key, ct);
    }
  }

  public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
  {
    if (!string.IsNullOrEmpty(tag))
    {
      await cache.RemoveByTagAsync(tag, ct);
    }
  }

  private static HybridCacheEntryOptions CreateOptions(TimeSpan? ttl)
  {
    var expiration = ttl is { Ticks: > 0 } ? ttl.Value : DefaultTtl;
    return new HybridCacheEntryOptions
    {
      Expiration = expiration,
      LocalCacheExpiration = expiration <= MaxLocalExpiration ? expiration : MaxLocalExpiration
    };
  }
}
