namespace Platform.Data;

public static class AgentMemoryCatalog
{
  public static string? DefaultDomainKnowledge { get; set; }

  public static AppAgentMemory[] DefaultRows() =>
  [
    new()
    {
      Kind = AgentMemoryKinds.Rule,
      Title = "Document & Image Line Extraction",
      Content = "When reading attached document or image files, extract every individual line item with its exact name, quantity, unit price, and line total, and match reference fields from the available dictionaries.",
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
        .Where(m => m.TenantId == null)
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

      if (!string.IsNullOrWhiteSpace(seedDomainKnowledge))
      {
        var trimmed = seedDomainKnowledge.Trim();
        if (trimmed.Length > 3800)
        {
          trimmed = trimmed[..3800];
        }

        var existingSeed = existing.FirstOrDefault(e => e.Kind == AgentMemoryKinds.Seed);
        if (existingSeed is null)
        {
          db.AppAgentMemory.Add(new AppAgentMemory
          {
            Kind = AgentMemoryKinds.Seed,
            Title = "Application Domain Knowledge & Workflows",
            Content = trimmed,
            Priority = 150,
            IsPinned = true,
            IsActive = true
          });
          changed = true;
        }
        else if (existingSeed.UpdatedAt == null &&
                 existingSeed.Ver <= 1 &&
                 !string.Equals(existingSeed.Content, trimmed, StringComparison.Ordinal))
        {
          existingSeed.Content = trimmed;
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
