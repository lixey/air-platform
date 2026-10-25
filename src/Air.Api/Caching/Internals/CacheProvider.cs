using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Api.Caching.Internals;

internal sealed class CacheProvider(
  IDistributedCache cache,
  ILogger<CacheProvider>? logger = null
) : ICacheProvider
{
  private const int MaxLocalEntries = 16_384;
  private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

  private readonly bool _isInMemory = cache is MemoryDistributedCache;
  private readonly ConcurrentDictionary<string, (object? Value, long ExpiresAtTicks)> _local = new(StringComparer.Ordinal);
  private readonly SemaphoreSlim[] _locks = CreateStripedLocks(256);
  private long _circuitDownUntilTicks;

  private bool IsDistributedAvailable =>
    Environment.TickCount64 >= Interlocked.Read(ref _circuitDownUntilTicks);

  public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return default;
    }

    if (_isInMemory)
    {
      return TryGetLocal<T>(key, out var val) ? val : default;
    }

    if (!IsDistributedAvailable)
    {
      return default;
    }

    try
    {
      var bytes = await cache.GetAsync(key, ct);
      return bytes is { Length: > 0 }
        ? JsonSerializer.Deserialize<T>(bytes, PlatformJsonOptions.DefaultOptions)
        : default;
    }
    catch (Exception ex)
    {
      TripCircuit(ex, key);
      return default;
    }
  }

  public async ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return;
    }

    var effectiveTtl = ttl is { Ticks: > 0 } ? ttl.Value : DefaultTtl;
    if (_isInMemory)
    {
      SetLocal(key, value, effectiveTtl);
      return;
    }

    if (!IsDistributedAvailable)
    {
      return;
    }

    try
    {
      var bytes = JsonSerializer.SerializeToUtf8Bytes(value, PlatformJsonOptions.DefaultOptions);
      await cache.SetAsync(
        key,
        bytes,
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = effectiveTtl },
        ct);
    }
    catch (Exception ex)
    {
      TripCircuit(ex, key);
    }
  }

  public async ValueTask<T?> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan? ttl = null,
    CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return await factory(ct);
    }

    if (_isInMemory)
    {
      if (TryGetLocal<T>(key, out var fastHit))
      {
        return fastHit;
      }
    }
    else if (IsDistributedAvailable)
    {
      var existing = await GetAsync<T>(key, ct);
      if (existing is not null)
      {
        return existing;
      }
    }

    var gate = _locks[string.GetHashCode(key, StringComparison.Ordinal) & 255];
    await gate.WaitAsync(ct);
    try
    {
      if (_isInMemory)
      {
        if (TryGetLocal<T>(key, out var recheckHit))
        {
          return recheckHit;
        }
      }
      else if (IsDistributedAvailable)
      {
        var recheck = await GetAsync<T>(key, ct);
        if (recheck is not null)
        {
          return recheck;
        }
      }

      var created = await factory(ct);
      if (created is not null)
      {
        await SetAsync(key, created, ttl, ct);
      }

      return created;
    }
    finally
    {
      gate.Release();
    }
  }

  public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(key))
    {
      return;
    }

    if (_isInMemory)
    {
      _local.TryRemove(key, out _);
      return;
    }

    if (!IsDistributedAvailable)
    {
      return;
    }

    try
    {
      await cache.RemoveAsync(key, ct);
    }
    catch (Exception ex)
    {
      TripCircuit(ex, key);
    }
  }

  private bool TryGetLocal<T>(string key, out T? value)
  {
    if (_local.TryGetValue(key, out var entry))
    {
      if (entry.ExpiresAtTicks > DateTimeOffset.UtcNow.UtcTicks)
      {
        value = entry.Value is T typed ? typed : default;
        return true;
      }

      _local.TryRemove(key, out _);
    }

    value = default;
    return false;
  }

  private void SetLocal<T>(string key, T value, TimeSpan ttl)
  {
    var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
    if (_local.Count >= MaxLocalEntries)
    {
      foreach (var kv in _local)
      {
        if (kv.Value.ExpiresAtTicks <= nowTicks)
        {
          _local.TryRemove(kv.Key, out _);
        }
      }

      if (_local.Count >= MaxLocalEntries)
      {
        _local.Clear();
      }
    }

    _local[key] = (value, nowTicks + ttl.Ticks);
  }

  private void TripCircuit(Exception ex, string key)
  {
    var until = Environment.TickCount64 + (long)CircuitBreakDuration.TotalMilliseconds;
    Interlocked.Exchange(ref _circuitDownUntilTicks, until);
    logger?.LogWarning(ex, "Distributed cache error on key '{Key}'; circuit open for {Seconds}s.", key, CircuitBreakDuration.TotalSeconds);
  }

  private static SemaphoreSlim[] CreateStripedLocks(int count)
  {
    var locks = new SemaphoreSlim[count];
    for (var i = 0; i < count; i++)
    {
      locks[i] = new SemaphoreSlim(1, 1);
    }

    return locks;
  }
}
