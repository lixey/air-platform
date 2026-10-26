namespace Platform.Agent;

public sealed class AgentModelSynchronizer(IEnumerable<IAgentProvider> providers) : IAgentModelSynchronizer
{
  private readonly SemaphoreSlim _syncLock = new(1, 1);

  public async Task<int> SyncFromProvidersAsync(
    PlatformDbContext db,
    CancellationToken ct = default,
    string? providerHandle = null)
  {
    await _syncLock.WaitAsync(ct);
    try
    {
      await AgentModelCatalog.EnsureSeededAsync(db, ct);

      var dbProviders = await db.AppAgentProvider
        .AsNoTracking()
        .ToListAsync(ct);
      var providersByHandle = dbProviders
        .ToDictionary(p => p.Handle, p => p, StringComparer.OrdinalIgnoreCase);

      var existing = await db.AppAgentModel.ToListAsync(ct);
      var nextOrder = existing.Count > 0 ? existing.Max(m => m.SortOrder) + 10 : 10;
      var added = 0;

      var targetProviders = !string.IsNullOrWhiteSpace(providerHandle)
        ? providers.Where(p => string.Equals(p.Handle, providerHandle, StringComparison.OrdinalIgnoreCase))
        : providers.Where(p => providersByHandle.TryGetValue(p.Handle, out var dp) && dp.IsActive);

      foreach (var provider in targetProviders)
      {
        var providerDisplayName = providersByHandle.TryGetValue(provider.Handle, out var dbProv) && !string.IsNullOrWhiteSpace(dbProv.Name)
          ? dbProv.Name
          : provider.Name;

        IReadOnlyList<AgentModelDto> models;
        try
        {
          models = await provider.GetModelsAsync(ct, forceRefresh: true);
        }
        catch
        {
          continue;
        }

        if (models.Count > 0)
        {
          var validIds = models
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

          var staleRows = existing
            .Where(e =>
              string.Equals(e.ProviderHandle, provider.Handle, StringComparison.OrdinalIgnoreCase) &&
              !validIds.Contains(e.ModelId) &&
              !e.IsDefault &&
              !e.IsForPhotos &&
              !e.UpdatedAt.HasValue)
            .ToList();

          foreach (var stale in staleRows)
          {
            db.AppAgentModel.Remove(stale);
            existing.Remove(stale);
          }
        }

        var isProviderActive = providersByHandle.TryGetValue(provider.Handle, out var provEntity) && provEntity.IsActive;

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
            row.ProviderName = providerDisplayName;
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
              ProviderName = providerDisplayName,
              ModelId = m.Id,
              Name = !string.IsNullOrWhiteSpace(m.Name) ? m.Name : m.Id,
              SortOrder = nextOrder,
              IsDefault = false,
              IsActive = isProviderActive
            };

            nextOrder += 10;
            db.AppAgentModel.Add(newRow);
            existing.Add(newRow);
            added++;
          }
        }
      }

      await db.SaveChangesAsync(ct);
      await AgentModelCatalog.EnsureSeededAsync(db, ct);
      return added;
    }
    finally
    {
      _syncLock.Release();
    }
  }
}
