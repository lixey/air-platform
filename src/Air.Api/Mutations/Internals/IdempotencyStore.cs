using System.Security.Cryptography;

namespace Platform.Api.Mutations.Internals;

internal static class IdempotencyStore
{
  private const int MaxKeyLength = 128;
  private const int MaxEntries = 100_000;
  private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
  private static readonly ConcurrentDictionary<string, KeyLock> KeyLocks = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, KeptEntry> Entries = new(StringComparer.Ordinal);
  private static long _lastPrunedTicks;

  public static string ComputePayloadHash(string rawPayload)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
    return Convert.ToHexString(bytes);
  }

  private static void ValidateKey(string key)
  {
    if (key.Length > MaxKeyLength)
    {
      throw new IdempotencyConflictException($"Idempotency-Key must not exceed {MaxKeyLength} characters.");
    }
  }

  public static async Task<IDisposable> AcquireLockAsync(long? tenantId, long? userId, string key, CancellationToken ct)
  {
    ValidateKey(key);
    var lockId = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    KeyLock entry;
    while (true)
    {
      entry = KeyLocks.GetOrAdd(lockId, static _ => new KeyLock());
      if (entry.TryAddRef())
      {
        break;
      }
    }

    try
    {
      await entry.Semaphore.WaitAsync(ct);
    }
    catch
    {
      ReleaseRef(lockId, entry);
      throw;
    }

    return new ReleaseHandle(lockId, entry);
  }

  public static bool TryGetCommitted(
    long? tenantId,
    long? userId,
    string serviceKey,
    string key,
    string payloadHash,
    out long recordId)
  {
    ValidateKey(key);
    PruneIfNeeded();
    var storeKey = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    if (!Entries.TryGetValue(storeKey, out var entry))
    {
      recordId = 0;
      return false;
    }

    if (DateTimeOffset.UtcNow - entry.CreatedAt > Retention)
    {
      Entries.TryRemove(storeKey, out _);
      recordId = 0;
      return false;
    }

    if (!string.Equals(entry.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(entry.PayloadHash, payloadHash, StringComparison.Ordinal))
    {
      throw new IdempotencyConflictException("Idempotency-Key has already been used for a different request.");
    }

    recordId = entry.RecordId;
    return true;
  }

  public static void SaveCommitted(
    long? tenantId,
    long? userId,
    string serviceKey,
    string key,
    string payloadHash,
    long recordId)
  {
    ValidateKey(key);
    PruneIfNeeded();
    if (Entries.Count >= MaxEntries)
    {
      PruneExpired(DateTimeOffset.UtcNow);
      if (Entries.Count >= MaxEntries)
      {
        foreach (var oldest in Entries.OrderBy(kv => kv.Value.CreatedAt).Take(Math.Max(1, MaxEntries / 10)))
        {
          Entries.TryRemove(oldest.Key, out _);
        }
      }
    }

    var storeKey = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    Entries[storeKey] = new KeptEntry(serviceKey, payloadHash, recordId, DateTimeOffset.UtcNow);
  }

  internal static void Clear()
  {
    Entries.Clear();
    KeyLocks.Clear();
  }

  private static void PruneIfNeeded()
  {
    var now = DateTimeOffset.UtcNow;
    var last = Interlocked.Read(ref _lastPrunedTicks);
    if (now.UtcTicks - last < TimeSpan.FromMinutes(10).Ticks)
    {
      return;
    }

    if (Interlocked.CompareExchange(ref _lastPrunedTicks, now.UtcTicks, last) != last)
    {
      return;
    }

    PruneExpired(now);
  }

  private static void PruneExpired(DateTimeOffset now)
  {
    foreach (var kv in Entries)
    {
      if (now - kv.Value.CreatedAt > Retention)
      {
        Entries.TryRemove(kv.Key, out _);
      }
    }
  }

  private static void ReleaseRef(string lockId, KeyLock entry)
  {
    if (entry.ReleaseRef())
    {
      KeyLocks.TryRemove(KeyValuePair.Create(lockId, entry));
    }
  }

  private sealed record KeptEntry(string ServiceKey, string PayloadHash, long RecordId, DateTimeOffset CreatedAt);

  private sealed class KeyLock
  {
    private int _refCount;
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public bool TryAddRef()
    {
      while (true)
      {
        var current = Volatile.Read(ref _refCount);
        if (current < 0)
        {
          return false;
        }

        if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
        {
          return true;
        }
      }
    }

    public bool ReleaseRef()
    {
      while (true)
      {
        var current = Volatile.Read(ref _refCount);
        if (current <= 1)
        {
          if (Interlocked.CompareExchange(ref _refCount, -1, current) == current)
          {
            return true;
          }
        }
        else if (Interlocked.CompareExchange(ref _refCount, current - 1, current) == current)
        {
          return false;
        }
      }
    }
  }

  private sealed class ReleaseHandle(string lockId, KeyLock entry) : IDisposable
  {
    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
      {
        try
        {
          entry.Semaphore.Release();
        }
        finally
        {
          ReleaseRef(lockId, entry);
        }
      }
    }
  }
}
