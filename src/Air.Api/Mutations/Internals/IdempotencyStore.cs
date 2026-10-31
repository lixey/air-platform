using System.Security.Cryptography;

namespace Platform.Api.Mutations.Internals;

internal static class IdempotencyStore
{
  private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
  private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, KeptEntry> Entries = new(StringComparer.Ordinal);
  private static long _lastPrunedTicks;

  public static string ComputePayloadHash(string rawPayload)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
    return Convert.ToHexString(bytes);
  }

  public static async Task<IDisposable> AcquireLockAsync(long? tenantId, long? userId, string key, CancellationToken ct)
  {
    var lockId = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    var sem = KeyLocks.GetOrAdd(lockId, static _ => new SemaphoreSlim(1, 1));
    await sem.WaitAsync(ct);
    return new ReleaseHandle(sem);
  }

  public static bool TryGetCommitted(
    long? tenantId,
    long? userId,
    string serviceKey,
    string key,
    string payloadHash,
    out long recordId)
  {
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
    PruneIfNeeded();
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

    foreach (var kv in Entries)
    {
      if (now - kv.Value.CreatedAt > Retention && Entries.TryRemove(kv.Key, out _))
      {
        if (KeyLocks.TryGetValue(kv.Key, out var sem) && sem.CurrentCount == 1)
        {
          KeyLocks.TryRemove(KeyValuePair.Create(kv.Key, sem));
        }
      }
    }
  }

  private sealed record KeptEntry(string ServiceKey, string PayloadHash, long RecordId, DateTimeOffset CreatedAt);

  private sealed class ReleaseHandle(SemaphoreSlim sem) : IDisposable
  {
    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
      {
        sem.Release();
      }
    }
  }
}
