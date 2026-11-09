using Platform.Agent.Internals;

namespace Platform.Agent;

public sealed class AgentStore
{
  private sealed record OwnedAttachment(AgentAttachmentDto Dto, AgentOwnerScope Owner, DateTimeOffset LastAccessUtc);

  private sealed record OwnedProposal(AgentProposalDto Dto, AgentOwnerScope Owner, DateTimeOffset LastAccessUtc);

  private sealed class SessionState(AgentOwnerScope owner)
  {
    public AgentOwnerScope Owner { get; } = owner;
    public DateTimeOffset LastAccessUtc { get; set; } = DateTimeOffset.UtcNow;
    public ConcurrentDictionary<string, string> ConversationIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AgentProposalDto> Proposals { get; } = [];
    public ConcurrentQueue<AgentProposalDto> PendingProposals { get; } = new();
    public ConcurrentQueue<string> SupersededProposalIds { get; } = new();
    public Dictionary<string, int> TurnStepCounters { get; } = new(StringComparer.OrdinalIgnoreCase);
  }

  private readonly AgentOptions _options;
  private readonly ConcurrentDictionary<string, OwnedAttachment> _attachments = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, OwnedProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
  private readonly SemaphoreSlim _seedLock = new(1, 1);
  private bool _catalogsSeeded;
  private long _lastCleanupTicks = DateTimeOffset.UtcNow.UtcTicks;

  public string WorkspaceDirectory { get; }

  public AgentStore(AgentOptions? options = null)
  {
    _options = options ?? new AgentOptions();
    WorkspaceDirectory = Path.Combine(Path.GetTempPath(), "air-agent-workspace");
    Directory.CreateDirectory(WorkspaceDirectory);
    CleanupStaleDiskFiles();
  }

  public async Task EnsureCatalogsSeededAsync(PlatformDbContext db, string? domainKnowledge, CancellationToken ct = default)
  {
    if (_catalogsSeeded)
    {
      return;
    }

    await _seedLock.WaitAsync(ct);
    try
    {
      if (_catalogsSeeded)
      {
        return;
      }

      await AgentModelCatalog.EnsureSeededAsync(db, ct);
      await AgentMemoryCatalog.EnsureSeededAsync(db, domainKnowledge, ct);
      _catalogsSeeded = true;
    }
    finally
    {
      _seedLock.Release();
    }
  }

  private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".pdf", ".txt", ".csv", ".json", ".xml", ".md"
  };

  private static readonly HashSet<string> BlockedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".msi", ".com", ".scr", ".vbs", ".js"
  };

  private const long MaxAttachmentSizeBytes = 25 * 1024 * 1024;

  public static AgentOwnerScope ResolveOwner(IServiceProvider services)
  {
    var tenant = services.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance;
    var user = services.GetService<IUserContext>();
    var tenantId = tenant?.TenantId;
    var userId = user is { IsAuthenticated: true } ? user.UserId : tenant?.UserId;
    return new AgentOwnerScope(tenantId, userId);
  }

  private static bool MatchesOwner(AgentOwnerScope entryOwner, AgentOwnerScope caller) =>
    entryOwner.TenantId == caller.TenantId && entryOwner.UserId == caller.UserId;

  private static string SessionKey(AgentOwnerScope owner, string sessionId) =>
    $"{owner.TenantId ?? 0}:{owner.UserId ?? 0}:{sessionId}";

  public string GetSessionWorkspaceDirectory(AgentOwnerScope owner, string? sessionId) =>
    GetSessionWorkspaceDirectory(WorkspaceDirectory, owner, sessionId);

  public static string GetSessionWorkspaceDirectory(string workspaceDirectory, AgentOwnerScope owner, string? sessionId)
  {
    var safeSessionId = NormalizeSessionId(sessionId);
    var dir = Path.Combine(workspaceDirectory, $"t_{owner.TenantId ?? 0}", $"u_{owner.UserId ?? 0}", safeSessionId);
    Directory.CreateDirectory(dir);
    return dir;
  }

  public static bool IsValidSessionId(string? sessionId)
  {
    if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64)
    {
      return false;
    }

    foreach (var ch in sessionId)
    {
      if (!char.IsLetterOrDigit(ch) && ch is not ('-' or '_'))
      {
        return false;
      }
    }

    return true;
  }

  public static string NormalizeSessionId(string? sessionId) =>
    IsValidSessionId(sessionId) ? sessionId! : Guid.NewGuid().ToString("N")[..12];

  public async Task<AgentAttachmentDto> SaveAttachmentAsync(
    AgentOwnerScope owner,
    string fileName,
    string contentType,
    Stream content,
    CancellationToken ct
  )
  {
    CleanupExpiredIfNeeded();

    var id = Guid.NewGuid().ToString("N")[..12];
    var ext = Path.GetExtension(fileName);
    if (!string.IsNullOrWhiteSpace(ext) && BlockedAttachmentExtensions.Contains(ext))
    {
      throw new InvalidOperationException(Localizer.Instance["FileTypeRefused"]);
    }

    if (string.IsNullOrWhiteSpace(ext) || !AllowedAttachmentExtensions.Contains(ext))
    {
      ext = contentType switch
      {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        "text/csv" => ".csv",
        "application/json" => ".json",
        _ => ".jpg"
      };
    }

    if (content.CanSeek && content.Length > MaxAttachmentSizeBytes)
    {
      throw new InvalidOperationException(Localizer.Instance["FileTooLarge"]);
    }

    var safeFileName = $"attachment-{id}{ext}";
    var dir = Path.Combine(WorkspaceDirectory, $"t_{owner.TenantId ?? 0}", $"u_{owner.UserId ?? 0}", "uploads");
    Directory.CreateDirectory(dir);
    var localPath = Path.Combine(dir, safeFileName);

    await using (var fs = File.Create(localPath))
    {
      await content.CopyToAsync(fs, ct);
      if (fs.Length > MaxAttachmentSizeBytes)
      {
        fs.Close();
        TryDeleteFile(localPath);
        throw new InvalidOperationException(Localizer.Instance["FileTooLarge"]);
      }
    }

    long fileSize;
    string sha256;
    await using (var readFs = File.OpenRead(localPath))
    {
      fileSize = readFs.Length;
      var hashBytes = await SHA256.HashDataAsync(readFs, ct);
      sha256 = Convert.ToHexStringLower(hashBytes);
    }

    var dto = new AgentAttachmentDto
    {
      Id = id,
      FileName = string.IsNullOrWhiteSpace(fileName) ? safeFileName : Path.GetFileName(fileName),
      ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
      Size = fileSize,
      PreviewUrl = $"/api/agent/attachments/{id}",
      LocalPath = localPath,
      Sha256 = sha256
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
      if (!string.IsNullOrWhiteSpace(proposal.TurnId) && proposal.StepNumber is null)
      {
        if (session.TurnStepCounters.Count > 64 && !session.TurnStepCounters.ContainsKey(proposal.TurnId))
        {
          session.TurnStepCounters.Clear();
        }

        var nextStep = session.TurnStepCounters.GetValueOrDefault(proposal.TurnId) + 1;
        session.TurnStepCounters[proposal.TurnId] = nextStep;
        proposal.StepNumber = nextStep;
      }

      session.Proposals.Add(proposal);
    }

    session.PendingProposals.Enqueue(proposal);
  }

  public void FinalizeTurnProposals(string sessionId, AgentOwnerScope owner, string turnId)
  {
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return;
    }

    lock (session.Proposals)
    {
      var turnList = session.Proposals
        .Where(p => string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (turnList.Count == 1)
      {
        turnList[0].StepNumber = null;
      }
    }
  }

  public IReadOnlyList<string> SupersedeOverlappingTurnProposals(
    string sessionId,
    AgentOwnerScope owner,
    string newTurnId,
    IReadOnlyCollection<(string Service, long RecordId)> editedRecords,
    IReadOnlyCollection<(string Service, string Name)> createdNames
  )
  {
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return [];
    }

    var superseded = new List<string>();
    lock (session.Proposals)
    {
      var pendingOld = session.Proposals
        .Where(p => p.Status == "pending" && !string.Equals(p.TurnId, newTurnId, StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (pendingOld.Count == 0)
      {
        return [];
      }

      static bool MatchesCandidate(
        AgentProposalDto p,
        IReadOnlyCollection<(string Service, long RecordId)> edits,
        IReadOnlyCollection<(string Service, string Name)> creates
      )
      {
        var normSvc = AgentFeatureRegistry.NormalizeServiceKey(p.Service);
        if (p.RecordId is > 0 && edits.Any(e =>
              e.RecordId == p.RecordId.Value && (string.IsNullOrEmpty(e.Service) || string.Equals(
                e.Service,
                normSvc,
                StringComparison.OrdinalIgnoreCase
              ))
            ))
        {
          return true;
        }

        if (p.RecordId is null)
        {
          var shortLabel = AgentStepGraphHelper.GetProposalShortLabel(p);
          if (creates.Any(c =>
                (string.IsNullOrEmpty(c.Service) || string.Equals(c.Service, normSvc, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(shortLabel, c.Name, StringComparison.OrdinalIgnoreCase)
              ))
          {
            return true;
          }
        }

        return false;
      }

      var matchedTurnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var matchedDirectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      foreach (var old in pendingOld)
      {
        if (MatchesCandidate(old, editedRecords, createdNames))
        {
          if (!string.IsNullOrEmpty(old.TurnId))
          {
            matchedTurnIds.Add(old.TurnId);
          }
          else
          {
            matchedDirectIds.Add(old.Id);
          }
        }
      }

      foreach (var old in pendingOld)
      {
        if (!string.IsNullOrEmpty(old.TurnId) && matchedTurnIds.Contains(old.TurnId) ||
            matchedDirectIds.Contains(old.Id))
        {
          lock (old)
          {
            if (old.Status == "pending")
            {
              old.Status = "superseded";
              superseded.Add(old.Id);
              session.SupersededProposalIds.Enqueue(old.Id);
            }
          }
        }
      }
    }

    return superseded;
  }

  public IReadOnlyList<string> DrainSupersededProposalIds(string sessionId, AgentOwnerScope owner)
  {
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return [];
    }

    var list = new List<string>();
    while (session.SupersededProposalIds.TryDequeue(out var id))
    {
      list.Add(id);
    }

    return list;
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

  public string? GetConversationId(string sessionId, AgentOwnerScope owner, string providerHandle)
  {
    CleanupExpiredIfNeeded();
    var sKey = SessionKey(owner, sessionId);
    if (!_sessions.TryGetValue(sKey, out var session) || !MatchesOwner(session.Owner, owner))
    {
      return null;
    }

    session.LastAccessUtc = DateTimeOffset.UtcNow;
    return session.ConversationIds.TryGetValue(providerHandle, out var cid) ? cid : null;
  }

  public void SetConversationId(string sessionId, AgentOwnerScope owner, string conversationId, string providerHandle)
  {
    var sKey = SessionKey(owner, sessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new SessionState(owner));
    session.LastAccessUtc = DateTimeOffset.UtcNow;
    session.ConversationIds[providerHandle] = conversationId;
  }

  public void RemoveConversationId(string sessionId, AgentOwnerScope owner, string providerHandle)
  {
    var sKey = SessionKey(owner, sessionId);
    if (_sessions.TryGetValue(sKey, out var session) && MatchesOwner(session.Owner, owner))
    {
      session.LastAccessUtc = DateTimeOffset.UtcNow;
      session.ConversationIds.TryRemove(providerHandle, out _);
    }
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

  public static string GetContextFilePath(string workspaceDirectory, AgentOwnerScope owner, string? sessionId) =>
    Path.Combine(GetSessionWorkspaceDirectory(workspaceDirectory, owner, sessionId), $"context-{NormalizeSessionId(sessionId)}.json");

  public static string GetRecordsFilePath(string workspaceDirectory, AgentOwnerScope owner, string? sessionId) =>
    Path.Combine(GetSessionWorkspaceDirectory(workspaceDirectory, owner, sessionId), $"records-{NormalizeSessionId(sessionId)}.json");

  private void CleanupStaleDiskFiles()
  {
    var cutoff = (DateTimeOffset.UtcNow - _options.SessionTtl).UtcDateTime;

    try
    {
      if (!Directory.Exists(WorkspaceDirectory))
      {
        return;
      }

      foreach (var file in Directory.EnumerateFiles(WorkspaceDirectory, "*", SearchOption.AllDirectories))
      {
        try
        {
          if (File.GetLastWriteTimeUtc(file) < cutoff)
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
