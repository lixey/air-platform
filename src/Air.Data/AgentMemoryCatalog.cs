namespace Platform.Data;

public static class AgentMemoryCatalog
{
  private static readonly ConcurrentDictionary<string, AgentDomainKnowledgeEntry> RegisteredKnowledge =
    new(StringComparer.OrdinalIgnoreCase);

  public static string? DefaultDomainKnowledge { get; set; }

  public static IReadOnlyList<AgentDomainKnowledgeEntry> DomainKnowledgeEntries =>
    RegisteredKnowledge.Values
      .OrderByDescending(e => e.Priority)
      .ThenBy(e => e.Module ?? string.Empty, StringComparer.OrdinalIgnoreCase)
      .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
      .ToList();

  public static void RegisterDomainKnowledge(
    string? module,
    string title,
    string content,
    int priority = 150)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(title);
    ArgumentException.ThrowIfNullOrWhiteSpace(content);
    var normalizedModule = string.IsNullOrWhiteSpace(module) ? null : module.Trim();
    if (normalizedModule is not null)
    {
      CodeRows.EnsurePascalCaseHandle(normalizedModule, nameof(module));
    }

    var normalizedTitle = title.Trim();
    var key = $"{normalizedModule ?? string.Empty}:{normalizedTitle}";
    RegisteredKnowledge[key] = new AgentDomainKnowledgeEntry(
      normalizedModule,
      normalizedTitle,
      content.Trim(),
      priority);
  }

  public static void ClearDomainKnowledge() => RegisteredKnowledge.Clear();

  public static AppAgentMemory[] DefaultRows() =>
  [
    new()
    {
      Kind = AgentMemoryKinds.Rule,
      Title = "Document & Image Line Extraction",
      Content = "When reading attached document or image files, extract every individual line item using the child form's declared fields, and match reference fields from the available dictionaries.",
      Priority = 200,
      IsPinned = true,
      IsActive = true
    },
    new()
    {
      Kind = AgentMemoryKinds.Rule,
      Title = "Proposal Confirmation Policy",
      Content = "Never mutate database records without user confirmation. Always emit <air_proposal> cards so the user can review fields, line items, and any duplicate record warnings before confirming.",
      Priority = 190,
      IsPinned = true,
      IsActive = true
    },
    new()
    {
      Kind = AgentMemoryKinds.Rule,
      Title = "Reference & Dictionary Resolution",
      Content = "Match reference fields by their existing dictionary IDs whenever possible. Only propose creating a new dictionary or reference item when no matching entry exists, and link dependent multi-step proposals via \"key\" and \"$ref:<key>\" so they confirm atomically.",
      Priority = 180,
      IsPinned = true,
      IsActive = true
    },
    new()
    {
      Kind = AgentMemoryKinds.Preference,
      Title = "Concise Analytical Formatting",
      Content = "Keep chat explanations concise (1-3 sentences) when presenting proposals, and use structured Markdown tables, <air_kpi>, and <air_chart> blocks when answering analytical or reporting questions.",
      Priority = 100,
      IsPinned = false,
      IsActive = true
    }
  ];

  public static async Task EnsureSeededAsync(
    PlatformDbContext db,
    string? seedDomainKnowledge = null,
    CancellationToken ct = default)
  {
    seedDomainKnowledge ??= DefaultDomainKnowledge;
    using (AsyncLocalTenantContext.UseSystemScope())
    {
      var existing = await db.AppAgentMemory
        .IgnoreQueryFilters()
        .Where(m => m.TenantId == null && m.UserId == null)
        .ToListAsync(ct);

      var changed = false;
      foreach (var seed in DefaultRows())
      {
        var match = existing.FirstOrDefault(e =>
          string.Equals(e.Title, seed.Title, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(e.Kind, seed.Kind, StringComparison.OrdinalIgnoreCase) &&
          e.Priority == seed.Priority &&
          e.UpdatedAt == null &&
          e.Ver <= 1);
        if (match is null)
        {
          db.AppAgentMemory.Add(seed);
          existing.Add(seed);
          changed = true;
        }
        else if (match.UpdatedAt == null &&
                 match.Ver <= 1 &&
                 (!string.Equals(match.Title, seed.Title, StringComparison.Ordinal) ||
                  !string.Equals(match.Content, seed.Content, StringComparison.Ordinal)))
        {
          match.Title = seed.Title;
          match.Content = seed.Content;
          changed = true;
        }
      }

      var entries = new List<AgentDomainKnowledgeEntry>(DomainKnowledgeEntries);
      if (!string.IsNullOrWhiteSpace(seedDomainKnowledge))
      {
        const string legacyTitle = "Application Domain Knowledge & Workflows";
        if (!entries.Any(e => e.Module is null && string.Equals(e.Title, legacyTitle, StringComparison.OrdinalIgnoreCase)))
        {
          entries.Add(new AgentDomainKnowledgeEntry(null, legacyTitle, seedDomainKnowledge.Trim(), 150));
        }
      }
      else if (entries.Count > 0)
      {
        var obsoleteLegacySeed = existing.FirstOrDefault(e =>
          e.Kind == AgentMemoryKinds.Seed &&
          e.Module == null &&
          string.Equals(e.Title, "Application Domain Knowledge & Workflows", StringComparison.OrdinalIgnoreCase));
        if (obsoleteLegacySeed is not null)
        {
          db.AppAgentMemory.Remove(obsoleteLegacySeed);
          existing.Remove(obsoleteLegacySeed);
          changed = true;
        }
      }

      foreach (var entry in entries)
      {
        var existingSeed = existing.FirstOrDefault(e =>
          e.Kind == AgentMemoryKinds.Seed &&
          string.Equals(e.Module ?? string.Empty, entry.Module ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(e.Title, entry.Title, StringComparison.OrdinalIgnoreCase));
        if (existingSeed is null)
        {
          var added = new AppAgentMemory
          {
            Kind = AgentMemoryKinds.Seed,
            Module = entry.Module,
            Title = entry.Title,
            Content = entry.Content,
            Priority = entry.Priority,
            IsPinned = true,
            IsActive = true
          };
          db.AppAgentMemory.Add(added);
          existing.Add(added);
          changed = true;
        }
        else if (!string.Equals(existingSeed.Content, entry.Content, StringComparison.Ordinal) ||
                 existingSeed.Priority != entry.Priority)
        {
          existingSeed.Content = entry.Content;
          existingSeed.Priority = entry.Priority;
          changed = true;
        }
      }

      if (changed)
      {
        await db.SaveChangesAsync(ct);
      }
    }
  }
}
