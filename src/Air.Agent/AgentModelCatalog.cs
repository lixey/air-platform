namespace Air.Agent;

public sealed class AgentModelSynchronizer(IEnumerable<IAgentProvider> providers) : IAgentModelSynchronizer
{
  public async Task<int> SyncFromProvidersAsync(PlatformDbContext db, CancellationToken ct = default)
  {
    await AgentModelCatalog.EnsureSeededAsync(db, ct);

    var existing = await db.AppAgentModel.ToListAsync(ct);
    var nextOrder = existing.Count > 0 ? existing.Max(m => m.SortOrder) + 10 : 50;
    var added = 0;

    foreach (var provider in providers)
    {
      IReadOnlyList<AgentModelDto> models;
      try
      {
        models = await provider.GetModelsAsync(ct, forceRefresh: true);
      }
      catch
      {
        continue;
      }

      foreach (var m in models)
      {
        if (string.IsNullOrWhiteSpace(m.Id))
        {
          continue;
        }

        var row = existing.FirstOrDefault(e =>
          string.Equals(e.ProviderHandle, provider.Handle, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(e.ModelId, m.Id, StringComparison.OrdinalIgnoreCase));

        if (row != null)
        {
          row.ProviderName = provider.Name;
          if (!string.IsNullOrWhiteSpace(m.Name))
          {
            row.Name = m.Name;
          }
        }
        else
        {
          var newRow = new AppAgentModel
          {
            ProviderHandle = provider.Handle,
            ProviderName = provider.Name,
            ModelId = m.Id,
            Name = !string.IsNullOrWhiteSpace(m.Name) ? m.Name : m.Id,
            SortOrder = nextOrder,
            IsDefault = false,
            IsActive = false
          };
          nextOrder += 10;
          db.AppAgentModel.Add(newRow);
          existing.Add(newRow);
          added++;
        }
      }
    }

    await db.SaveChangesAsync(ct);
    return added;
  }
}
