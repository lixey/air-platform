using Platform.Agent.Internals;

namespace Platform.Agent;

public sealed class AgentStore
{
  private readonly ConcurrentDictionary<string, OwnedAttachment> _attachments = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, OwnedProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, AgentSessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
  private readonly SemaphoreSlim _seedLock = new(1, 1);
  private readonly SemaphoreSlim _globalTurnSemaphore;
  private readonly TimeProvider _time;
  private bool _catalogsSeeded;
  private long _lastAutoSyncTicks;
  private long _lastCleanupTicks;

  public AgentOptions Options { get; }
  public string WorkspaceDirectory { get; }
  internal PendingMemoryStore PendingMemories { get; }

  public AgentStore(AgentOptions? options = null, TimeProvider? time = null)
  {
    Options = options ?? new AgentOptions();
    _time = time ?? TimeProvider.System;
    PendingMemories = new PendingMemoryStore(_time);
    _lastCleanupTicks = _time.GetUtcNow().UtcTicks;
    var maxTurns = Math.Max(1, Options.MaxConcurrentTurns);
    _globalTurnSemaphore = new SemaphoreSlim(maxTurns, maxTurns);

    var appHash = Convert.ToHexStringLower(
      SHA256.HashData(Encoding.UTF8.GetBytes($"{AppContext.BaseDirectory}|{Environment.UserName}")))[..12];
    WorkspaceDirectory = Path.Combine(Path.GetTempPath(), $"platform-agent-{appHash}");
    EnsurePrivateDirectory(WorkspaceDirectory);
    AgentAttachmentStorage.CleanupStaleDiskFiles(WorkspaceDirectory, (_time.GetUtcNow() - Options.SessionTtl).UtcDateTime);
  }

  internal static void EnsurePrivateDirectory(string path) =>
    AgentAttachmentStorage.EnsurePrivateDirectory(path);

  public bool TryAcquireSessionTurn(string? sessionId, AgentOwnerScope owner, out IDisposable? lease)
  {
    var safeSessionId = NormalizeSessionId(sessionId);
    var sKey = SessionKey(owner, safeSessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new AgentSessionState(owner));
    session.LastAccessUtc = _time.GetUtcNow();
    if (!session.TurnGate.Wait(0))
    {
      lease = null;
      return false;
    }

    lease = new AgentTurnLease(session.TurnGate);
    return true;
  }

  public async Task<IDisposable> AcquireGlobalTurnSlotAsync(CancellationToken ct = default)
  {
    await _globalTurnSemaphore.WaitAsync(ct);
    return new AgentTurnLease(_globalTurnSemaphore);
  }

  public bool TryClaimAutoSyncCooldown(TimeSpan cooldown)
  {
    var nowTicks = _time.GetUtcNow().UtcTicks;
    var prev = Interlocked.Read(ref _lastAutoSyncTicks);
    if (nowTicks - prev < cooldown.Ticks)
    {
      return false;
    }

    return Interlocked.CompareExchange(ref _lastAutoSyncTicks, nowTicks, prev) == prev;
  }

  public async Task EnsureCatalogsSeededAsync(PlatformDbContext db, string? domainKnowledge = null, CancellationToken ct = default)
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

      using var _ = AsyncLocalTenantContext.UseSystemScope(correlationId: "agent-catalog-seed");
      await AgentModelCatalog.EnsureSeededAsync(db, ct);
      await AgentMemoryCatalog.EnsureSeededAsync(db, domainKnowledge ?? Options.DomainKnowledge, ct: ct);
      _catalogsSeeded = true;
    }
    finally
    {
      _seedLock.Release();
    }
  }

  public static AgentOwnerScope ResolveOwner(IServiceProvider services)
  {
    var bridgeCtx = services.GetService<McpExecutionBridge>()?.Active;
    if (bridgeCtx is { TurnToken.Length: > 0 })
    {
      return new AgentOwnerScope(bridgeCtx.TenantId, bridgeCtx.UserId);
    }

    var tenant = services.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance;
    var user = services.GetService<IUserContext>();
    var tenantId = tenant?.TenantId;
    var userId = user is { IsAuthenticated: true } ? user.UserId : tenant?.UserId;
    return new AgentOwnerScope(tenantId, userId);
  }

  private static bool MatchesOwner(AgentOwnerScope entryOwner, AgentOwnerScope caller) =>
    entryOwner.TenantId == caller.TenantId && entryOwner.UserId == caller.UserId;

  private static string SessionKey(AgentOwnerScope owner, string sessionId) =>
    $"{AgentAttachmentStorage.FormatTenantSegment(owner.TenantId)}:{AgentAttachmentStorage.FormatUserSegment(owner.UserId)}:{sessionId}";

  public string GetSessionWorkspaceDirectory(AgentOwnerScope owner, string? sessionId) =>
    GetSessionWorkspaceDirectory(WorkspaceDirectory, owner, sessionId);

  public static string GetSessionWorkspaceDirectory(string workspaceDirectory, AgentOwnerScope owner, string? sessionId) =>
    AgentAttachmentStorage.GetSessionWorkspaceDirectory(workspaceDirectory, owner, NormalizeSessionId(sessionId));

  public string GetSessionAttachmentsDirectory(AgentOwnerScope owner, string? sessionId) =>
    GetSessionAttachmentsDirectory(WorkspaceDirectory, owner, sessionId);

  public static string GetSessionAttachmentsDirectory(string workspaceDirectory, AgentOwnerScope owner, string? sessionId) =>
    AgentAttachmentStorage.GetSessionAttachmentsDirectory(workspaceDirectory, owner, NormalizeSessionId(sessionId));

  public string CreateTurnWorkspaceDirectory(AgentOwnerScope owner, string? sessionId)
  {
    var sessionDir = GetSessionWorkspaceDirectory(owner, sessionId);
    var turnDir = Path.Combine(sessionDir, $"turn-{Guid.NewGuid():N}");
    EnsurePrivateDirectory(turnDir);
    return turnDir;
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
    IsValidSessionId(sessionId) ? sessionId! : Guid.NewGuid().ToString("N");

  public async Task<AgentAttachmentDto> SaveAttachmentAsync(
    AgentOwnerScope owner,
    string fileName,
    string contentType,
    Stream content,
    CancellationToken ct)
  {
    CleanupExpiredIfNeeded();
    var dto = await AgentAttachmentStorage.WriteValidatedAttachmentAsync(
      WorkspaceDirectory, owner, fileName, contentType, content, ct);
    _attachments[dto.Id] = new OwnedAttachment(dto, owner, _time.GetUtcNow());
    return dto;
  }

  public AgentAttachmentDto? GetAttachment(string id, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    if (!_attachments.TryGetValue(id, out var entry) || !MatchesOwner(entry.Owner, owner))
    {
      return null;
    }

    _attachments[id] = entry with { LastAccessUtc = _time.GetUtcNow() };
    return entry.Dto;
  }

  public void StageProposal(AgentProposalDto proposal, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    var now = _time.GetUtcNow();
    var seal = ProposalSeal.Compute(proposal, owner);
    _proposals[proposal.Id] = new OwnedProposal(proposal, owner, now, seal);

    var sKey = SessionKey(owner, proposal.SessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new AgentSessionState(owner));
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
    IReadOnlyCollection<(string Service, string Name)> createdNames)
  {
    var sKey = SessionKey(owner, sessionId);
    return _sessions.TryGetValue(sKey, out var session) && MatchesOwner(session.Owner, owner)
      ? AgentProposalSuperseder.SupersedeOverlapping(session, newTurnId, editedRecords, createdNames)
      : [];
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

    _proposals[proposalId] = entry with { LastAccessUtc = _time.GetUtcNow() };
    return entry.Dto;
  }

  internal AgentProposalDto? GetVerifiedProposal(string proposalId, AgentOwnerScope owner)
  {
    CleanupExpiredIfNeeded();
    if (!_proposals.TryGetValue(proposalId, out var entry) || !MatchesOwner(entry.Owner, owner))
    {
      return null;
    }

    if (!ProposalSeal.Verify(entry.Dto, owner, entry.Seal))
    {
      throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
    }

    _proposals[proposalId] = entry with { LastAccessUtc = _time.GetUtcNow() };
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

    session.LastAccessUtc = _time.GetUtcNow();
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

    session.LastAccessUtc = _time.GetUtcNow();
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

    session.LastAccessUtc = _time.GetUtcNow();
    return session.ConversationIds.TryGetValue(providerHandle, out var cid) ? cid : null;
  }

  public void SetConversationId(string sessionId, AgentOwnerScope owner, string conversationId, string providerHandle)
  {
    var sKey = SessionKey(owner, sessionId);
    var session = _sessions.GetOrAdd(sKey, _ => new AgentSessionState(owner));
    session.LastAccessUtc = _time.GetUtcNow();
    session.ConversationIds[providerHandle] = conversationId;
  }

  public void RemoveConversationId(string sessionId, AgentOwnerScope owner, string providerHandle)
  {
    var sKey = SessionKey(owner, sessionId);
    if (_sessions.TryGetValue(sKey, out var session) && MatchesOwner(session.Owner, owner))
    {
      session.LastAccessUtc = _time.GetUtcNow();
      session.ConversationIds.TryRemove(providerHandle, out _);
    }
  }

  public void ResetSession(string sessionId, AgentOwnerScope owner)
  {
    var sKey = SessionKey(owner, sessionId);
    if (_sessions.TryGetValue(sKey, out var existing) && existing.TurnGate.CurrentCount == 0)
    {
      return;
    }

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

    if (IsValidSessionId(sessionId))
    {
      AgentAttachmentStorage.TryDeleteSessionDirectory(WorkspaceDirectory, owner, sessionId);
    }
  }

  public static string GetContextFilePath(string turnOrSessionDirectory) =>
    Path.Combine(turnOrSessionDirectory, "context.json");

  public static string GetRecordsFilePath(string turnOrSessionDirectory) =>
    Path.Combine(turnOrSessionDirectory, "records.json");

  public static string GetSyncContextFilePath(string turnOrSessionDirectory) =>
    Path.Combine(turnOrSessionDirectory, "sync-context.json");

  private void CleanupExpiredIfNeeded()
  {
    var now = _time.GetUtcNow();
    var prevTicks = Interlocked.Read(ref _lastCleanupTicks);
    if (now.UtcTicks - prevTicks < TimeSpan.FromMinutes(5).Ticks ||
        Interlocked.CompareExchange(ref _lastCleanupTicks, now.UtcTicks, prevTicks) != prevTicks)
    {
      return;
    }

    var cutoff = now - Options.SessionTtl;
    foreach (var (key, session) in _sessions)
    {
      if (session.LastAccessUtc < cutoff && session.TurnGate.CurrentCount > 0)
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
        AgentAttachmentStorage.TryDeleteFile(removed.Dto.LocalPath);
      }
    }
  }
}
