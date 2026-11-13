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
    const string startTag = "<agent_memory>";
    const string endTag = "</agent_memory>";
    if (string.IsNullOrWhiteSpace(text) ||
        text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase) < 0 ||
        request.Services is not { } services ||
        services.GetService<DbScope>() == null && services.GetService<PlatformDbContext>() == null)
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

    const int maxPersonalMemories = 100;
    var personalCount = await db.AppAgentMemory
      .IgnoreQueryFilters()
      .CountAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);

    var jsonOpts = new JsonDocumentOptions
    {
      AllowTrailingCommas = true,
      CommentHandling = JsonCommentHandling.Skip
    };

    var savedEntities = new List<AppAgentMemory>();
    var idx = 0;

    while (idx < text.Length)
    {
      var start = text.IndexOf(startTag, idx, StringComparison.OrdinalIgnoreCase);
      if (start < 0)
      {
        break;
      }

      var end = text.IndexOf(endTag, start + startTag.Length, StringComparison.OrdinalIgnoreCase);
      if (end < 0)
      {
        break;
      }

      var json = text[(start + startTag.Length)..end].Trim();
      idx = end + endTag.Length;
      if (string.IsNullOrWhiteSpace(json))
      {
        continue;
      }

      try
      {
        using var doc = JsonDocument.Parse(json, jsonOpts);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var title = root.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String
          ? tProp.GetString()?.Trim()
          : null;
        var content = root.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String
          ? cProp.GetString()?.Trim()
          : null;
        var rawKind = root.TryGetProperty("kind", out var kProp) && kProp.ValueKind == JsonValueKind.String
          ? kProp.GetString()?.Trim()
          : null;
        var rawCategory = root.TryGetProperty("category", out var catProp) && catProp.ValueKind == JsonValueKind.String
          ? catProp.GetString()?.Trim()
          : null;
        var rawScope = root.TryGetProperty("scope", out var sProp) && sProp.ValueKind == JsonValueKind.String
          ? sProp.GetString()?.Trim()
          : null;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
          continue;
        }

        // Silent <agent_memory> writes of tenant/global Rules or Seeds are strictly rejected (#8 Decision #3).
        if (string.Equals(rawKind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawKind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawCategory, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawCategory, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawScope, "tenant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawScope, "global", StringComparison.OrdinalIgnoreCase) ||
            root.TryGetProperty("userId", out var uProp) && uProp.ValueKind == JsonValueKind.Null)
        {
          continue;
        }

        var effectiveKind = !string.IsNullOrWhiteSpace(rawKind) ? rawKind : rawCategory;
        var kind = string.Equals(effectiveKind, AgentMemoryKinds.Fact, StringComparison.OrdinalIgnoreCase)
          ? AgentMemoryKinds.Fact
          : string.IsNullOrWhiteSpace(effectiveKind) || string.Equals(effectiveKind, AgentMemoryKinds.Preference, StringComparison.OrdinalIgnoreCase)
            ? AgentMemoryKinds.Preference
            : null;
        if (kind == null)
        {
          continue;
        }

        if (title.Length > 200)
        {
          title = title[..200];
        }

        if (content.Length > 8000)
        {
          content = content[..8000];
        }

        var existing = await db.AppAgentMemory
          .IgnoreQueryFilters()
          .FirstOrDefaultAsync(m => m.Title == title && m.TenantId == tenantId && m.UserId == userId, ct);
        if (existing is not null)
        {
          if (existing.IsPinned ||
              string.Equals(existing.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(existing.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase))
          {
            continue;
          }

          existing.Kind = kind;
          existing.Content = content;
          existing.IsActive = true;
          if (!savedEntities.Contains(existing))
          {
            savedEntities.Add(existing);
          }
        }
        else
        {
          if (personalCount >= maxPersonalMemories)
          {
            continue;
          }

          personalCount++;
          var created = new AppAgentMemory
          {
            TenantId = tenantId,
            UserId = userId,
            Kind = kind,
            Title = title,
            Content = content,
            Priority = 100,
            IsPinned = false,
            IsActive = true
          };
          db.AppAgentMemory.Add(created);
          savedEntities.Add(created);
        }
      }
      catch
      {
        // Ignore malformed memory blocks
      }
    }

    if (savedEntities.Count == 0)
    {
      return [];
    }

    try
    {
      await db.SaveChangesAsync(ct);
      return savedEntities
        .Where(e => e.Id > 0)
        .Select(e => new AgentSavedMemoryDto
        {
          Id = e.Id,
          Category = e.Kind,
          Title = e.Title,
          Content = e.Content
        })
        .ToList();
    }
    catch
    {
      return [];
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
