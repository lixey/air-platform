namespace Air.Agent;

public sealed class AgentStore
{
  private sealed record OwnedAttachment(AgentAttachmentDto Dto, AgentOwnerScope Owner, DateTimeOffset LastAccessUtc);
  private sealed record OwnedProposal(AgentProposalDto Dto, AgentOwnerScope Owner, DateTimeOffset LastAccessUtc);

  private sealed class SessionState(AgentOwnerScope owner)
  {
    public AgentOwnerScope Owner { get; } = owner;
    public DateTimeOffset LastAccessUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ConversationId { get; set; }
    public List<AgentProposalDto> Proposals { get; } = [];
    public ConcurrentQueue<AgentProposalDto> PendingProposals { get; } = new();
  }

  private readonly AgentOptions _options;
  private readonly ConcurrentDictionary<string, OwnedAttachment> _attachments = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, OwnedProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
  private long _lastCleanupTicks = DateTimeOffset.UtcNow.UtcTicks;

  public string WorkspaceDirectory { get; }

  public AgentStore(AgentOptions? options = null)
  {
    _options = options ?? new AgentOptions();
    WorkspaceDirectory = Path.Combine(Path.GetTempPath(), "air-agent-workspace");
    Directory.CreateDirectory(WorkspaceDirectory);
    CleanupStaleDiskFiles();
  }

  public static AgentOwnerScope ResolveOwner(IServiceProvider services)
  {
    var tenant = services.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance;
    var user = services.GetService<IUserContext>();
    var tenantId = tenant?.TenantId ?? 1;
    var userId = user is { IsAuthenticated: true } ? user.UserId : tenant?.UserId;
    return new AgentOwnerScope(tenantId, userId);
  }

  private static bool MatchesOwner(AgentOwnerScope entryOwner, AgentOwnerScope caller)
  {
    if (entryOwner.TenantId != caller.TenantId)
    {
      return false;
    }

    if (entryOwner.UserId.HasValue && caller.UserId.HasValue && entryOwner.UserId.Value != caller.UserId.Value)
    {
      return false;
    }

    if (entryOwner.UserId.HasValue && !caller.UserId.HasValue)
    {
      return false;
    }

    return true;
  }

  private static string SessionKey(AgentOwnerScope owner, string sessionId) =>
    $"{owner.TenantId}:{owner.UserId ?? 0}:{sessionId}";

  public async Task<AgentAttachmentDto> SaveAttachmentAsync(
    AgentOwnerScope owner,
    string fileName,
    string contentType,
    Stream content,
    CancellationToken ct)
  {
    CleanupExpiredIfNeeded();

    var id = Guid.NewGuid().ToString("N")[..12];
    var ext = Path.GetExtension(fileName);
    if (string.IsNullOrWhiteSpace(ext))
    {
      ext = contentType switch
      {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
      };
    }

    var safeFileName = $"attachment-{id}{ext}";
    var dir = Path.Combine(WorkspaceDirectory, "uploads");
    Directory.CreateDirectory(dir);
    var localPath = Path.Combine(dir, safeFileName);

    await using (var fs = File.Create(localPath))
    {
      await content.CopyToAsync(fs, ct);
    }

    var fileInfo = new FileInfo(localPath);
    var dto = new AgentAttachmentDto
    {
      Id = id,
      FileName = string.IsNullOrWhiteSpace(fileName) ? safeFileName : fileName,
      ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
      Size = fileInfo.Length,
      PreviewUrl = $"/api/agent/attachments/{id}",
      LocalPath = localPath
    };

    _attachments[id] = new OwnedAttachment(dto, owner, DateTimeOffset.UtcNow);
    return dto;
  }

  public AgentAttachmentDto? GetAttachment(string id, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    if (!_attachments.TryGetValue(id, out var entry) || !MatchesOwner(entry.Owner, owner))
    {
      return null;
    }

    _attachments[id] = entry with { LastAccessUtc = DateTimeOffset.UtcNow };
    return entry.Dto;
  }

  public void StageProposal(AgentProposalDto proposal, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    var now = DateTimeOffset.UtcNow;
    _proposals[proposal.Id] = new OwnedProposal(proposal, owner, now);

    var sKey = SessionKey(owner, proposal.SessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new SessionState(owner));
    session.LastAccessUtc = now;
    lock (session.Proposals)
    {
      session.Proposals.Add(proposal);
    }

    session.PendingProposals.Enqueue(proposal);
  }

  public AgentProposalDto? GetProposal(string proposalId, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    if (!_proposals.TryGetValue(proposalId, out var entry) || !MatchesOwner(entry.Owner, owner))
    {
      return null;
    }

    _proposals[proposalId] = entry with { LastAccessUtc = DateTimeOffset.UtcNow };
    return entry.Dto;
  }

  public IReadOnlyList<AgentProposalDto> GetSessionProposals(string sessionId, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return [];
    }

    session.LastAccessUtc = DateTimeOffset.UtcNow;
    lock (session.Proposals)
    {
      return [.. session.Proposals];
    }
  }

  public IReadOnlyList<AgentProposalDto> DrainPendingProposals(string sessionId, AgentOwnerScope owner)
  {
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return [];
    }

    session.LastAccessUtc = DateTimeOffset.UtcNow;
    var list = new List<AgentProposalDto>();
    while (session.PendingProposals.TryDequeue(out var p))
    {
      list.Add(p);
    }

    return list;
  }

  public string? GetConversationId(string sessionId, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return null;
    }

    session.LastAccessUtc = DateTimeOffset.UtcNow;
    return session.ConversationId;
  }

  public void SetConversationId(string sessionId, AgentOwnerScope owner, string conversationId)
  {
    var sKey = SessionKey(owner, sessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new SessionState(owner));
    session.LastAccessUtc = DateTimeOffset.UtcNow;
    session.ConversationId = conversationId;
  }

  public void ResetSession(string sessionId, AgentOwnerScope owner)
  {
    var sKey = SessionKey(owner, sessionId);
    if (_sessions.TryRemove(sKey, out var session))
    {
      lock (session.Proposals)
      {
        foreach (var p in session.Proposals)
        {
          _proposals.TryRemove(p.Id, out _);
        }
      }
    }
  }

  private void CleanupExpiredIfNeeded()
  {
    var now = DateTimeOffset.UtcNow;
    var prevTicks = Interlocked.Read(ref _lastCleanupTicks);
    if (now.UtcTicks - prevTicks < TimeSpan.FromMinutes(5).Ticks)
    {
      return;
    }

    if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.UtcTicks, prevTicks) != prevTicks)
    {
      return;
    }

    var cutoff = now - _options.SessionTtl;

    foreach (var (key, session) in _sessions)
    {
      if (session.LastAccessUtc < cutoff)
      {
        _sessions.TryRemove(key, out _);
      }
    }

    foreach (var (key, prop) in _proposals)
    {
      if (prop.LastAccessUtc < cutoff)
      {
        _proposals.TryRemove(key, out _);
      }
    }

    foreach (var (key, att) in _attachments)
    {
      if (att.LastAccessUtc < cutoff && _attachments.TryRemove(key, out var removed))
      {
        TryDeleteFile(removed.Dto.LocalPath);
      }
    }
  }

  private void CleanupStaleDiskFiles()
  {
    try
    {
      var dir = Path.Combine(WorkspaceDirectory, "uploads");
      if (!Directory.Exists(dir))
      {
        return;
      }

      var cutoff = DateTimeOffset.UtcNow - _options.SessionTtl;
      foreach (var file in Directory.GetFiles(dir))
      {
        try
        {
          if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
          {
            File.Delete(file);
          }
        }
        catch { }
      }
    }
    catch { }
  }

  private static void TryDeleteFile(string? path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return;
    }

    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch { }
  }
}
