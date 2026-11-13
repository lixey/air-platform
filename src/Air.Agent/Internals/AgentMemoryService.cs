namespace Platform.Agent.Internals;

internal static class AgentMemoryService
{
  public static async Task AppendDatabaseMemoryAndDomainKnowledgeAsync(
    StringBuilder sb,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    var options = request.Services?.GetService<AgentOptions>();
    var maxChars = options?.MaxMemoryChars ?? 12_000;
    var hasDbSeed = false;

    if (request.Services is { } services &&
        (services.GetService<DbScope>() != null || services.GetService<PlatformDbContext>() != null))
    {
      try
      {
        await using var memScope = AgentModuleScope.OpenDefault(services);
        var db = memScope.Db;
        var ownerTenantId = request.Owner.TenantId;
        var ownerUserId = request.Owner.UserId;
        var memories = await db.AppAgentMemory
          .IgnoreQueryFilters()
          .AsNoTracking()
          .Where(m => m.IsActive &&
                      (m.TenantId == null || ownerTenantId != null && m.TenantId == ownerTenantId) &&
                      (m.UserId == null || ownerUserId != null && m.UserId == ownerUserId))
          .OrderByDescending(m => m.Kind == AgentMemoryKinds.Rule && m.UserId == null)
          .ThenByDescending(m => m.IsPinned)
          .ThenByDescending(m => m.Priority)
          .ThenBy(m => m.Id)
          .ToListAsync(ct);

        if (memories.Count > 0)
        {
          var usedChars = 0;
          var rules = new List<AppAgentMemory>();
          var knowledge = new List<AppAgentMemory>();

          foreach (var mem in memories)
          {
            var entryLen = mem.Title.Length + mem.Content.Length + 16;
            if (usedChars + entryLen > maxChars)
            {
              continue;
            }

            usedChars += entryLen;
            if (mem.UserId == null && string.Equals(mem.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase))
            {
              rules.Add(mem);
            }
            else
            {
              if (string.Equals(mem.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase))
              {
                hasDbSeed = true;
              }

              knowledge.Add(mem);
            }
          }

          if (rules.Count > 0)
          {
            sb.AppendLine();
            sb.AppendLine("[MANDATORY AI RULES & POLICIES (FROM DATABASE)]");
            foreach (var r in rules)
            {
              sb.AppendLine($"- [{r.Title}]: {r.Content}");
            }
          }

          if (knowledge.Count > 0)
          {
            sb.AppendLine();
            sb.AppendLine("[PERSISTENT AI MEMORY & DOMAIN KNOWLEDGE (FROM DATABASE)]");
            foreach (var k in knowledge)
            {
              sb.AppendLine($"### {k.Title} ({k.Kind})");
              sb.AppendLine(k.Content);
            }
          }
        }
      }
      catch
      {
        // Ignore if table is not yet migrated
      }
    }

    if (!hasDbSeed)
    {
      var registeredEntries = AgentMemoryCatalog.ResolveDomainKnowledgeEntries(request.Services?.GetService<ModuleCatalog>());
      if (registeredEntries.Count > 0)
      {
        sb.AppendLine();
        sb.AppendLine("[APPLICATION DOMAIN KNOWLEDGE]");
        foreach (var entry in registeredEntries)
        {
          sb.AppendLine($"### {entry.Title}");
          sb.AppendLine(entry.Content);
        }
      }
      else
      {
        var domainKnowledge = options?.DomainKnowledge ?? AgentMemoryCatalog.DefaultDomainKnowledge;
        if (!string.IsNullOrWhiteSpace(domainKnowledge))
        {
          sb.AppendLine();
          sb.AppendLine("[APPLICATION DOMAIN KNOWLEDGE]");
          sb.AppendLine(domainKnowledge);
        }
      }
    }
  }

  public static async Task<IReadOnlyList<AgentSavedMemoryDto>> SaveInlineMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    if (!AgentMemoryTagParser.ContainsTag(text) ||
        request.Services is not { } services ||
        services.GetService<DbScope>() == null && services.GetService<PlatformDbContext>() == null)
    {
      return [];
    }

    var candidates = AgentMemoryTagParser.ParsePersonalCandidates(text);
    if (candidates.Count == 0)
    {
      return [];
    }

    await using var scope = AgentModuleScope.OpenDefault(services);
    var db = scope.Db;
    var app = scope.App;
    if (app.User is not { IsAuthenticated: true, UserId: > 0 })
    {
      return [];
    }

    var userId = app.User.UserId.Value;
    var tenantId = request.Owner.TenantId ?? db.TenantContext.TenantId;
    if (db.SetOfBase<BaseAppTenant>() != null && tenantId is null or <= 0)
    {
      return [];
    }

    var store = services.GetService<AgentStore>();
    if (store is null)
    {
      return [];
    }

    const int maxPersonalMemories = 100;
    const int maxInlinePerTurn = 5;
    var personalCount = await db.AppAgentMemory
      .IgnoreQueryFilters()
      .CountAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);

    var stagedItems = new List<(string Kind, string Title, string Content)>();
    foreach (var (kind, title, content) in candidates)
    {
      var existing = await db.AppAgentMemory
        .IgnoreQueryFilters()
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.Title == title && m.TenantId == tenantId && m.UserId == userId, ct);
      if (existing is not null)
      {
        if (AgentMemoryTagParser.IsProtectedRow(existing))
        {
          continue;
        }
      }
      else if (personalCount >= maxPersonalMemories)
      {
        continue;
      }
      else
      {
        personalCount++;
      }

      var existingIdx = stagedItems.FindIndex(item => string.Equals(item.Title, title, StringComparison.Ordinal));
      if (existingIdx >= 0)
      {
        stagedItems[existingIdx] = (kind, title, content);
      }
      else if (stagedItems.Count < maxInlinePerTurn)
      {
        stagedItems.Add((kind, title, content));
      }
    }

    if (stagedItems.Count == 0)
    {
      return [];
    }

    var pendingStore = store.PendingMemories;
    var pendingTtl = store.Options.SessionTtl;
    var owner = new AgentOwnerScope(tenantId, userId);
    var results = new List<AgentSavedMemoryDto>(stagedItems.Count);
    foreach (var (kind, title, content) in stagedItems)
    {
      var token = pendingStore.Stage(owner, kind, title, content, pendingTtl);
      results.Add(new AgentSavedMemoryDto
      {
        Id = 0,
        Category = kind,
        Title = title,
        Content = content,
        Status = AgentSavedMemoryStatus.Pending,
        PendingToken = token
      });
    }

    return results;
  }

  public static async Task<AgentSavedMemoryDto?> ConfirmInlineMemoryAsync(
    string? pendingToken,
    AgentOwnerScope owner,
    IServiceProvider services,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(pendingToken) ||
        owner.UserId is not > 0 ||
        services.GetService<DbScope>() == null && services.GetService<PlatformDbContext>() == null)
    {
      return null;
    }

    await using var scope = AgentModuleScope.OpenDefault(services);
    var db = scope.Db;
    var tenantId = owner.TenantId ?? db.TenantContext.TenantId;
    if (db.SetOfBase<BaseAppTenant>() != null && tenantId is null or <= 0)
    {
      return null;
    }

    var store = services.GetService<AgentStore>();
    if (store is null)
    {
      return null;
    }

    var pendingStore = store.PendingMemories;
    var pendingTtl = store.Options.SessionTtl;
    var effectiveOwner = owner with { TenantId = tenantId };
    if (!pendingStore.TryClaim(pendingToken, effectiveOwner, pendingTtl, out var pending) || pending is null)
    {
      return null;
    }

    var completed = false;
    try
    {
      var userId = owner.UserId.Value;
      var existing = await db.AppAgentMemory
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(m => m.Title == pending.Title && m.TenantId == tenantId && m.UserId == userId, ct);

      AppAgentMemory entity;
      if (existing is not null)
      {
        if (AgentMemoryTagParser.IsProtectedRow(existing))
        {
          return null;
        }

        existing.Kind = pending.Kind;
        existing.Content = pending.Content;
        existing.IsActive = true;
        entity = existing;
      }
      else
      {
        const int maxPersonalMemories = 100;
        var personalCount = await db.AppAgentMemory
          .IgnoreQueryFilters()
          .CountAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
        if (personalCount >= maxPersonalMemories)
        {
          return null;
        }

        entity = new AppAgentMemory
        {
          TenantId = tenantId,
          UserId = userId,
          Kind = pending.Kind,
          Title = pending.Title,
          Content = pending.Content,
          Priority = 100,
          IsPinned = false,
          IsActive = true
        };
        db.AppAgentMemory.Add(entity);
      }

      await db.SaveChangesAsync(ct);
      pendingStore.Complete(pendingToken);
      completed = true;
      return new AgentSavedMemoryDto
      {
        Id = entity.Id,
        Category = entity.Kind,
        Title = entity.Title,
        Content = entity.Content,
        Status = AgentSavedMemoryStatus.Saved
      };
    }
    finally
    {
      if (!completed)
      {
        pendingStore.Release(pendingToken);
      }
    }
  }

  public static async Task<bool> UndoPersonalMemoryAsync(
    long memoryId,
    AgentOwnerScope owner,
    IServiceProvider services,
    CancellationToken ct)
  {
    if (memoryId <= 0 ||
        owner.UserId is not > 0 ||
        services.GetService<DbScope>() == null && services.GetService<PlatformDbContext>() == null)
    {
      return false;
    }

    await using var scope = AgentModuleScope.OpenDefault(services);
    var db = scope.Db;
    if (db.SetOfBase<BaseAppTenant>() != null && owner.TenantId is null or <= 0)
    {
      return false;
    }

    var row = await db.AppAgentMemory
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(
        m => m.Id == memoryId &&
             m.TenantId == owner.TenantId &&
             m.UserId == owner.UserId &&
             m.UserId != null,
        ct);

    if (row == null ||
        string.Equals(row.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    db.AppAgentMemory.Remove(row);
    await db.SaveChangesAsync(ct);
    return true;
  }
}
