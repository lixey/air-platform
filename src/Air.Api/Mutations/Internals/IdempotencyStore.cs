using System.Security.Cryptography;

namespace Platform.Api.Mutations.Internals;

internal sealed class IdempotencyStore(TimeProvider? time = null)
{
  public static IdempotencyStore Default { get; } = new();

  private const int MaxKeyLength = 128;
  private const int MaxEntries = 100_000;
  private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

  private readonly TimeProvider _time = time ?? TimeProvider.System;
  private readonly ConcurrentDictionary<string, IdempotencyKeyLock> _keyLocks = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, IdempotencyKeptEntry> _entries = new(StringComparer.Ordinal);
  private long _lastPrunedTicks;

  public static string ComputePayloadHash(string rawPayload)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
    return Convert.ToHexString(bytes);
  }

  public async Task<IDisposable> AcquireLockAsync(long? tenantId, long? userId, string key, CancellationToken ct)
  {
    ValidateKey(key);
    var lockId = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    IdempotencyKeyLock entry;
    while (true)
    {
      entry = _keyLocks.GetOrAdd(lockId, static _ => new IdempotencyKeyLock());
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

    return new IdempotencyReleaseHandle(lockId, entry, ReleaseRef);
  }

  public bool TryGetCommitted(
    long? tenantId,
    long? userId,
    string serviceKey,
    string key,
    string payloadHash,
    out long recordId,
    out object? cachedResult)
  {
    ValidateKey(key);
    PruneIfNeeded();
    var storeKey = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    if (!_entries.TryGetValue(storeKey, out var entry))
    {
      recordId = 0;
      cachedResult = null;
      return false;
    }

    if (_time.GetUtcNow() - entry.CreatedAt > Retention)
    {
      _entries.TryRemove(storeKey, out _);
      recordId = 0;
      cachedResult = null;
      return false;
    }

    if (!string.Equals(entry.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(entry.PayloadHash, payloadHash, StringComparison.Ordinal))
    {
      throw new IdempotencyConflictException("Idempotency-Key has already been used for a different request.");
    }

    recordId = entry.RecordId;
    cachedResult = entry.CachedResult;
    return true;
  }

  public void SaveCommitted(
    long? tenantId,
    long? userId,
    string serviceKey,
    string key,
    string payloadHash,
    long recordId,
    object? cachedResult = null)
  {
    ValidateKey(key);
    var now = _time.GetUtcNow();
    PruneIfNeeded();
    if (_entries.Count >= MaxEntries)
    {
      PruneExpired(now);
      if (_entries.Count >= MaxEntries)
      {
        foreach (var oldest in _entries.OrderBy(kv => kv.Value.CreatedAt).Take(Math.Max(1, MaxEntries / 10)))
        {
          _entries.TryRemove(oldest.Key, out _);
        }
      }
    }

    var storeKey = $"{tenantId ?? 0}:{userId ?? 0}:{key}";
    _entries[storeKey] = new IdempotencyKeptEntry(serviceKey, payloadHash, recordId, now, recordId > 0 ? null : cachedResult);
  }

  internal void Clear()
  {
    _entries.Clear();
    _keyLocks.Clear();
  }

  private static void ValidateKey(string key)
  {
    if (key.Length > MaxKeyLength)
    {
      throw new IdempotencyConflictException($"Idempotency-Key must not exceed {MaxKeyLength} characters.");
    }
  }

  private void PruneIfNeeded()
  {
    var now = _time.GetUtcNow();
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

  private void PruneExpired(DateTimeOffset now)
  {
    foreach (var kv in _entries)
    {
      if (now - kv.Value.CreatedAt > Retention)
      {
        _entries.TryRemove(kv.Key, out _);
      }
    }
  }

  private void ReleaseRef(string lockId, IdempotencyKeyLock entry)
  {
    if (entry.ReleaseRef())
    {
      _keyLocks.TryRemove(KeyValuePair.Create(lockId, entry));
    }
  }
}
