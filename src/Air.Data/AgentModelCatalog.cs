namespace Air.Data;

public interface IAgentModelSynchronizer
{
  Task<int> SyncFromProvidersAsync(PlatformDbContext db, CancellationToken ct = default);
}

public static class AgentModelCatalog
{
  public const string DefaultModelId = "gemini-4-argon-high";

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
    }
  ];

  public static async Task EnsureSeededAsync(PlatformDbContext db, CancellationToken ct = default)
  {
    if (await db.AppAgentModel.AnyAsync(ct))
    {
      return;
    }

    db.AppAgentModel.AddRange(DefaultRows());
    await db.SaveChangesAsync(ct);
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
}
