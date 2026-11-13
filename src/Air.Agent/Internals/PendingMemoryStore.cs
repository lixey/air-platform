namespace Platform.Agent.Internals;

internal sealed class PendingMemoryStore(TimeProvider? time = null)
{
  public const int MaxEntriesPerOwner = 16;
  private const int MaxEntries = 2048;

  private readonly Lock _gate = new();
  private readonly Dictionary<string, PendingMemoryEntry> _entries = new(StringComparer.Ordinal);
  private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
  private readonly TimeProvider _time = time ?? TimeProvider.System;

  public string Stage(AgentOwnerScope owner, string kind, string title, string content, TimeSpan ttl)
  {
    var now = _time.GetUtcNow();
    var token = Guid.NewGuid().ToString("N");
    lock (_gate)
    {
      PruneExpired(now, ttl);
      PruneOwnerOverflow(owner);
      PruneGlobalOverflow();
      _entries[token] = new PendingMemoryEntry(owner, kind, title, content, now);
    }

    return token;
  }

  public bool TryClaim(string? pendingToken, AgentOwnerScope owner, TimeSpan ttl, out PendingMemoryEntry? entry)
  {
    entry = null;
    if (string.IsNullOrWhiteSpace(pendingToken))
    {
      return false;
    }

    var now = _time.GetUtcNow();
    lock (_gate)
    {
      if (!_entries.TryGetValue(pendingToken, out var existing))
      {
        return false;
      }

      if (now - existing.CreatedAtUtc > ttl)
      {
        _entries.Remove(pendingToken);
        _inFlight.Remove(pendingToken);
        return false;
      }

      if (existing.Owner.TenantId != owner.TenantId || existing.Owner.UserId != owner.UserId)
      {
        return false;
      }

      if (!_inFlight.Add(pendingToken))
      {
        return false;
      }

      entry = existing;
      return true;
    }
  }

  public void Complete(string pendingToken)
  {
    lock (_gate)
    {
      _entries.Remove(pendingToken);
      _inFlight.Remove(pendingToken);
    }
  }

  public void Release(string pendingToken)
  {
    lock (_gate)
    {
      _inFlight.Remove(pendingToken);
    }
  }

  private void PruneExpired(DateTimeOffset now, TimeSpan ttl)
  {
    List<string>? expired = null;
    foreach (var (key, item) in _entries)
    {
      if (now - item.CreatedAtUtc > ttl && !_inFlight.Contains(key))
      {
        (expired ??= []).Add(key);
      }
    }

    if (expired is null)
    {
      return;
    }

    foreach (var key in expired)
    {
      _entries.Remove(key);
    }
  }

  private void PruneOwnerOverflow(AgentOwnerScope owner)
  {
    var ownerKeys = _entries
      .Where(kv => kv.Value.Owner.TenantId == owner.TenantId && kv.Value.Owner.UserId == owner.UserId && !_inFlight.Contains(kv.Key))
      .OrderBy(kv => kv.Value.CreatedAtUtc)
      .Select(kv => kv.Key)
      .ToList();
    var excess = ownerKeys.Count - MaxEntriesPerOwner + 1;
    for (var i = 0; i < excess; i++)
    {
      _entries.Remove(ownerKeys[i]);
    }
  }

  private void PruneGlobalOverflow()
  {
    if (_entries.Count < MaxEntries)
    {
      return;
    }

    var oldest = _entries
      .Where(kv => !_inFlight.Contains(kv.Key))
      .OrderBy(kv => kv.Value.CreatedAtUtc)
      .Take(_entries.Count - MaxEntries + 1)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in oldest)
    {
      _entries.Remove(key);
    }
  }
}
