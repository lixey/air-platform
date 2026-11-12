namespace Platform.Api.Auth.Internals;

internal sealed class LoginRateLimiter(TimeProvider? time = null)
{
  public const int MaxAccountFailuresPerIp = 20;
  public const int MaxTotalFailuresPerIp = 100;
  private const int MaxEntries = 50_000;
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

  private readonly TimeProvider _time = time ?? TimeProvider.System;
  private readonly ConcurrentDictionary<string, LoginRateBucket> _buckets = new(StringComparer.Ordinal);
  private long _lastPrunedTicks;

  public bool IsLimited(string? ipAddress, string? login = null)
  {
    var normalizedIp = NormalizeIp(ipAddress);
    if (normalizedIp == null)
    {
      return false;
    }

    var now = _time.GetUtcNow();
    if (IsBucketLimited(GlobalIpKey(normalizedIp), MaxTotalFailuresPerIp, now))
    {
      return true;
    }

    return IsBucketLimited(PairKey(normalizedIp, login), MaxAccountFailuresPerIp, now);
  }

  public void RecordFailure(string? ipAddress, string? login = null)
  {
    var normalizedIp = NormalizeIp(ipAddress);
    if (normalizedIp == null)
    {
      return;
    }

    var now = _time.GetUtcNow();
    PruneIfNeeded(now);

    IncrementBucket(GlobalIpKey(normalizedIp), now);
    IncrementBucket(PairKey(normalizedIp, login), now);
  }

  public void Reset(string? ipAddress, string? login = null)
  {
    var normalizedIp = NormalizeIp(ipAddress);
    if (normalizedIp == null)
    {
      return;
    }

    _buckets.TryRemove(PairKey(normalizedIp, login), out _);
    if (string.IsNullOrWhiteSpace(login))
    {
      _buckets.TryRemove(GlobalIpKey(normalizedIp), out _);
    }
  }

  private bool IsBucketLimited(string key, int limit, DateTimeOffset now)
  {
    if (!_buckets.TryGetValue(key, out var bucket))
    {
      return false;
    }

    if (now - bucket.WindowStart >= Window)
    {
      _buckets.TryRemove(KeyValuePair.Create(key, bucket));
      return false;
    }

    return bucket.Failures >= limit;
  }

  private void IncrementBucket(string key, DateTimeOffset now)
  {
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

  private static string? NormalizeIp(string? ipAddress) =>
    string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim();

  private static string NormalizeLogin(string? login) =>
    string.IsNullOrWhiteSpace(login) ? "_" : login.Trim().ToLowerInvariant();

  private static string GlobalIpKey(string normalizedIp) => $"ip:{normalizedIp}";

  private static string PairKey(string normalizedIp, string? login) =>
    $"pair:{normalizedIp}:{NormalizeLogin(login)}";

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
