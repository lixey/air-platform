namespace Platform.Data;

public static class AgentModelCatalog
{
  public const string DefaultModelId = "gemini-4-argon-high";
  public const string DefaultPhotoModelId = "gemini-3.8-flash-high";

  public static AppAgentProvider[] DefaultProviders() =>
  [
    new()
    {
      Handle = "Agy",
      Name = "Antigravity CLI (agy)",
      SortOrder = 10,
      IsActive = true
    },
    new()
    {
      Handle = "Claude",
      Name = "Claude CLI (claude)",
      SortOrder = 20,
      IsActive = false
    },
    new()
    {
      Handle = "Codex",
      Name = "Codex CLI (codex)",
      SortOrder = 30,
      IsActive = false
    },
    new()
    {
      Handle = "LmStudio",
      Name = "LM Studio / Local OpenAI",
      SortOrder = 40,
      IsActive = false
    }
  ];

  public static AppAgentModel[] DefaultRows() =>
  [
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-4-argon-high",
      Name = "Gemini 4 Argon (High)",
      SortOrder = 10,
      IsDefault = true,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-3.8-flash-high",
      Name = "Gemini 3.8 Flash (High)",
      SortOrder = 20,
      IsDefault = false,
      IsForPhotos = true,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-opus-5-5-high",
      Name = "Claude Opus 5.5 (High)",
      SortOrder = 30,
      IsDefault = false,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-sonnet-5-5-high",
      Name = "Claude Sonnet 5.5 (High)",
      SortOrder = 40,
      IsDefault = false,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-4-argon-medium",
      Name = "Gemini 4 Argon (Medium)",
      SortOrder = 50,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-4-argon-low",
      Name = "Gemini 4 Argon (Low)",
      SortOrder = 60,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-3.8-flash-medium",
      Name = "Gemini 3.8 Flash (Medium)",
      SortOrder = 22,
      IsDefault = false,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-3.8-flash-low",
      Name = "Gemini 3.8 Flash (Low)",
      SortOrder = 25,
      IsDefault = false,
      IsForPhotos = false,
      IsActive = true
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-opus-5-5-medium",
      Name = "Claude Opus 5.5 (Medium)",
      SortOrder = 90,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-opus-5-5-low",
      Name = "Claude Opus 5.5 (Low)",
      SortOrder = 100,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-sonnet-5-5-medium",
      Name = "Claude Sonnet 5.5 (Medium)",
      SortOrder = 110,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "claude-sonnet-5-5-low",
      Name = "Claude Sonnet 5.5 (Low)",
      SortOrder = 120,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gpt-oss-120b-medium",
      Name = "GPT-OSS 120B (Medium)",
      SortOrder = 130,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Claude",
      ProviderName = "Claude CLI (claude)",
      ModelId = "claude-opus-5-5-high",
      Name = "Claude Opus 5.5 (High)",
      SortOrder = 190,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Claude",
      ProviderName = "Claude CLI (claude)",
      ModelId = "claude-sonnet-5-5-high",
      Name = "Claude Sonnet 5.5 (High)",
      SortOrder = 195,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Claude",
      ProviderName = "Claude CLI (claude)",
      ModelId = "claude-opus-4-6",
      Name = "Claude Opus 4.6",
      SortOrder = 200,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Claude",
      ProviderName = "Claude CLI (claude)",
      ModelId = "claude-sonnet-4-6",
      Name = "Claude Sonnet 4.6",
      SortOrder = 210,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Codex",
      ProviderName = "Codex CLI (codex)",
      ModelId = "gpt-5.3-codex",
      Name = "GPT-5.3 Codex",
      SortOrder = 300,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Codex",
      ProviderName = "Codex CLI (codex)",
      ModelId = "o4-mini-high",
      Name = "OpenAI o4-mini (High)",
      SortOrder = 310,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "LmStudio",
      ProviderName = "LM Studio / Local OpenAI",
      ModelId = "qwen3-coder-32b",
      Name = "Qwen 3 Coder 32B (Local)",
      SortOrder = 400,
      IsDefault = false,
      IsActive = false
    }
  ];

  private static int _schemaEnsured;

  public static async Task<HashSet<string>> GetActiveProviderHandlesAsync(
    PlatformDbContext db,
    CancellationToken ct = default)
  {
    var list = await db.AppAgentProvider
      .AsNoTracking()
      .Where(p => p.IsActive)
      .Select(p => p.Handle)
      .ToListAsync(ct);
    return list.ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  public static async Task EnsureSeededAsync(PlatformDbContext db, CancellationToken ct = default)
  {
    if (Interlocked.CompareExchange(ref _schemaEnsured, 1, 0) == 0)
    {
      try
      {
        if (db.Database.IsRelational())
        {
          await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AppAgentProvider" (
              "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "Handle" character varying(64) NOT NULL DEFAULT '',
              "Name" character varying(128) NOT NULL DEFAULT '',
              "SortOrder" integer NOT NULL DEFAULT 0,
              "IsActive" boolean NOT NULL DEFAULT true,
              "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
              "UpdatedAt" timestamp with time zone NULL,
              "Ver" integer NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppAgentProvider_Handle" ON "AppAgentProvider" ("Handle");
            ALTER TABLE "AppAgentModel" ADD COLUMN IF NOT EXISTS "IsForPhotos" boolean NOT NULL DEFAULT false;
            """,
            ct);
        }
      }
      catch
      {
        Interlocked.Exchange(ref _schemaEnsured, 0);
      }
    }

    var existingProviders = await db.AppAgentProvider.ToListAsync(ct);
    var existing = await db.AppAgentModel.ToListAsync(ct);
    var changed = false;

    foreach (var seedProvider in DefaultProviders())
    {
      var foundProvider = existingProviders.FirstOrDefault(p =>
        string.Equals(p.Handle, seedProvider.Handle, StringComparison.OrdinalIgnoreCase));

      if (foundProvider == null)
      {
        db.AppAgentProvider.Add(seedProvider);
        existingProviders.Add(seedProvider);
        changed = true;
      }
    }

    foreach (var seed in DefaultRows())
    {
      var found = existing.FirstOrDefault(e =>
        string.Equals(e.ProviderHandle, seed.ProviderHandle, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(e.ModelId, seed.ModelId, StringComparison.OrdinalIgnoreCase));

      if (found == null)
      {
        if (seed.IsDefault && existing.Any(e => e.IsDefault))
        {
          seed.IsDefault = false;
        }

        if (seed.IsForPhotos && existing.Any(e => e.IsForPhotos))
        {
          seed.IsForPhotos = false;
        }

        db.AppAgentModel.Add(seed);
        existing.Add(seed);
        changed = true;
      }
    }

    SyncProviderModelsStateCore(existingProviders, existing, ref changed);

    if (changed)
    {
      await db.SaveChangesAsync(ct);
    }
  }

  public static async Task SyncProviderModelsStateAsync(PlatformDbContext db, CancellationToken ct = default)
  {
    var existingProviders = await db.AppAgentProvider.ToListAsync(ct);
    var existingModels = await db.AppAgentModel.ToListAsync(ct);
    var changed = false;

    SyncProviderModelsStateCore(existingProviders, existingModels, ref changed);

    if (changed)
    {
      await db.SaveChangesAsync(ct);
    }
  }

  private static void SyncProviderModelsStateCore(
    List<AppAgentProvider> providers,
    List<AppAgentModel> models,
    ref bool changed)
  {
    var activeProviderHandles = providers
      .Where(p => p.IsActive)
      .Select(p => p.Handle)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var m in models)
    {
      if (!activeProviderHandles.Contains(m.ProviderHandle))
      {
        if (m.IsActive || m.IsDefault || m.IsForPhotos)
        {
          m.IsActive = false;
          m.IsDefault = false;
          m.IsForPhotos = false;
          changed = true;
        }
      }
    }

    var activeProviderModels = models
      .Where(m => activeProviderHandles.Contains(m.ProviderHandle))
      .ToList();

    if (activeProviderModels.Count > 0 && !activeProviderModels.Any(e => e.IsDefault))
    {
      var defRow = activeProviderModels.FirstOrDefault(e =>
          string.Equals(e.ModelId, DefaultModelId, StringComparison.OrdinalIgnoreCase))
        ?? activeProviderModels.FirstOrDefault(e => e.IsActive)
        ?? activeProviderModels[0];
      defRow.IsDefault = true;
      defRow.IsActive = true;
      changed = true;
    }

    if (activeProviderModels.Count > 0 && !activeProviderModels.Any(e => e.IsForPhotos))
    {
      var photoRow = activeProviderModels.FirstOrDefault(e =>
          string.Equals(e.ModelId, DefaultPhotoModelId, StringComparison.OrdinalIgnoreCase))
        ?? activeProviderModels.FirstOrDefault(e => e.IsDefault)
        ?? activeProviderModels[0];
      photoRow.IsForPhotos = true;
      photoRow.IsActive = true;
      changed = true;
    }
  }

  public static async Task<bool> SetDefaultModelAsync(
    PlatformDbContext db,
    long modelRowId,
    CancellationToken ct = default)
  {
    var activeProviderHandles = await GetActiveProviderHandlesAsync(db, ct);

    var all = await db.AppAgentModel.ToListAsync(ct);
    var target = all.FirstOrDefault(r => r.Id == modelRowId);
    if (target == null || !activeProviderHandles.Contains(target.ProviderHandle))
    {
      return false;
    }

    foreach (var row in all)
    {
      if (row.Id == modelRowId)
      {
        row.IsDefault = true;
        row.IsActive = true;
      }
      else if (row.IsDefault)
      {
        row.IsDefault = false;
      }
    }

    await db.SaveChangesAsync(ct);
    return true;
  }

  public static async Task<bool> SetPhotoModelAsync(
    PlatformDbContext db,
    long modelRowId,
    CancellationToken ct = default)
  {
    var activeProviderHandles = await GetActiveProviderHandlesAsync(db, ct);

    var all = await db.AppAgentModel.ToListAsync(ct);
    var target = all.FirstOrDefault(r => r.Id == modelRowId);
    if (target == null || !activeProviderHandles.Contains(target.ProviderHandle))
    {
      return false;
    }

    foreach (var row in all)
    {
      if (row.Id == modelRowId)
      {
        row.IsForPhotos = true;
        row.IsActive = true;
      }
      else if (row.IsForPhotos)
      {
        row.IsForPhotos = false;
      }
    }

    await db.SaveChangesAsync(ct);
    return true;
  }
}
