namespace Air.Data;

public interface IAgentModelSynchronizer
{
  Task<int> SyncFromProvidersAsync(PlatformDbContext db, CancellationToken ct = default);
}

public static class AgentModelCatalog
{
  public const string DefaultModelId = "gemini-4-argon-high";
  public const string DefaultPhotoModelId = "gemini-3.8-flash-low";

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
      SortOrder = 70,
      IsDefault = false,
      IsActive = false
    },
    new()
    {
      ProviderHandle = "Agy",
      ProviderName = "Antigravity CLI (agy)",
      ModelId = "gemini-3.8-flash-low",
      Name = "Gemini 3.8 Flash (Low)",
      SortOrder = 25,
      IsDefault = false,
      IsForPhotos = true,
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

  public static async Task EnsureSeededAsync(PlatformDbContext db, CancellationToken ct = default)
  {
    try
    {
      if (db.Database.IsRelational())
      {
        await db.Database.ExecuteSqlRawAsync(
          """ALTER TABLE "AppAgentModel" ADD COLUMN IF NOT EXISTS "IsForPhotos" boolean NOT NULL DEFAULT false;""",
          ct);
      }
    }
    catch
    {
      // Ignore if provider is not PostgreSQL or column already exists
    }

    var existing = await db.AppAgentModel.ToListAsync(ct);
    var changed = false;

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

    if (existing.Count > 0 && !existing.Any(e => e.IsForPhotos))
    {
      var photoRow = existing.FirstOrDefault(e => string.Equals(e.ModelId, DefaultPhotoModelId, StringComparison.OrdinalIgnoreCase))
        ?? existing.FirstOrDefault(e => e.IsDefault)
        ?? existing[0];
      photoRow.IsForPhotos = true;
      photoRow.IsActive = true;
      changed = true;
    }

    if (changed)
    {
      await db.SaveChangesAsync(ct);
    }
  }

  public static async Task SetDefaultModelAsync(
    PlatformDbContext db,
    long modelRowId,
    CancellationToken ct = default)
  {
    var all = await db.AppAgentModel.ToListAsync(ct);
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
  }

  public static async Task SetPhotoModelAsync(
    PlatformDbContext db,
    long modelRowId,
    CancellationToken ct = default)
  {
    var all = await db.AppAgentModel.ToListAsync(ct);
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
  }
}
