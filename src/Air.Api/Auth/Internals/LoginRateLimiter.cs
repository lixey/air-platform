namespace Platform.Api.Auth.Internals;

internal sealed class LoginRateLimiter(TimeProvider? time = null)
{
  private const int MaxFailedAttempts = 20;
  private const int MaxEntries = 50_000;
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

  private readonly TimeProvider _time = time ?? TimeProvider.System;
  private readonly ConcurrentDictionary<string, LoginRateBucket> _buckets = new(StringComparer.Ordinal);
  private long _lastPrunedTicks;

  public bool IsLimited(string? ipAddress)
  {
    var key = NormalizeKey(ipAddress);
    if (key == null)
    {
      return false;
    }

    var now = _time.GetUtcNow();
    if (!_buckets.TryGetValue(key, out var bucket))
    {
      return false;
    }

    if (now - bucket.WindowStart >= Window)
    {
      _buckets.TryRemove(KeyValuePair.Create(key, bucket));
      return false;
    }

    return bucket.Failures >= MaxFailedAttempts;
  }

  public void RecordFailure(string? ipAddress)
  {
    var key = NormalizeKey(ipAddress);
    if (key == null)
    {
      return;
    }

    var now = _time.GetUtcNow();
    PruneIfNeeded(now);
    if (_buckets.Count >= MaxEntries && !_buckets.ContainsKey(key))
    {
      PruneExpired(now);
      if (_buckets.Count >= MaxEntries)
      {
        foreach (var oldest in _buckets.OrderBy(kv => kv.Value.WindowStart).Take(Math.Max(1, MaxEntries / 10)))
        {
          _buckets.TryRemove(oldest.Key, out _);
        }
      }
    }

    _buckets.AddOrUpdate(
      key,
      static (_, ts) => new LoginRateBucket(1, ts),
      static (_, existing, ts) => ts - existing.WindowStart >= Window
        ? new LoginRateBucket(1, ts)
        : existing with { Failures = existing.Failures + 1 },
      now);
  }

  public void Reset(string? ipAddress)
  {
    var key = NormalizeKey(ipAddress);
    if (key != null)
    {
      _buckets.TryRemove(key, out _);
    }
  }

  private static string? NormalizeKey(string? ipAddress) =>
    string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim();

  private void PruneIfNeeded(DateTimeOffset now)
  {
    var last = Interlocked.Read(ref _lastPrunedTicks);
    if (now.UtcTicks - last < Window.Ticks)
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
    foreach (var kv in _buckets)
    {
      if (now - kv.Value.WindowStart >= Window)
      {
        _buckets.TryRemove(kv.Key, out _);
      }
    }
  }
}
