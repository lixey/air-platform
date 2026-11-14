namespace Platform.Agent.Internals;

internal static class AgentTurnDeltaTracker
{
  public static string ComputeSessionFingerprint(IServiceProvider? services, AgentOwnerScope owner)
  {
    var bridge = services?.GetService<McpExecutionBridge>()?.Active;
    var tenantCtx = services?.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance;

    var rights = bridge?.Rights ?? tenantCtx?.Rights;

    var sortedRights = rights != null
      ? string.Join(',', rights.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
      : string.Empty;

    var raw = string.Create(
      CultureInfo.InvariantCulture,
      $"t={owner.TenantId}|u={owner.UserId}|c={CultureInfo.CurrentUICulture.Name}|r={sortedRights}");

    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
  }

  public static async Task<(string ChangedBlock, Dictionary<long, string> Snapshot)> CollectMemorySnapshotAsync(
    IServiceProvider? services,
    AgentOwnerScope owner,
    IReadOnlyDictionary<long, string>? previousSnapshot,
    CancellationToken ct)
  {
    var currentSnapshot = new Dictionary<long, string>();
    if (services == null || services.GetService<DbScope>() == null && services.GetService<PlatformDbContext>() == null)
    {
      return (string.Empty, currentSnapshot);
    }

    List<AppAgentMemory> memories;
    try
    {
      await using var memScope = AgentModuleScope.OpenDefault(services);
      var ownerTenantId = owner.TenantId;
      var ownerUserId = owner.UserId;
      memories = await memScope.Db.AppAgentMemory
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(m => m.IsActive &&
                    (m.TenantId == null || ownerTenantId != null && m.TenantId == ownerTenantId) &&
                    (m.UserId == null || ownerUserId != null && m.UserId == ownerUserId))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return (string.Empty, currentSnapshot);
    }

    var changed = new List<AppAgentMemory>();
    foreach (var mem in memories)
    {
      var hash = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{mem.Kind}|{mem.Title}|{mem.Content}")))[..16];
      currentSnapshot[mem.Id] = hash;
      if (previousSnapshot != null &&
          (!previousSnapshot.TryGetValue(mem.Id, out var prevHash) || !string.Equals(prevHash, hash, StringComparison.Ordinal)))
      {
        changed.Add(mem);
      }
    }

    var removedIds = previousSnapshot != null
      ? previousSnapshot.Keys.Where(id => !currentSnapshot.ContainsKey(id)).ToList()
      : [];

    if (previousSnapshot == null || changed.Count == 0 && removedIds.Count == 0)
    {
      return (string.Empty, currentSnapshot);
    }

    var sb = new StringBuilder();
    sb.AppendLine("[UPDATED AI MEMORIES & RULES]");
    foreach (var mem in changed)
    {
      sb.AppendLine($"- ({mem.Kind}) [{mem.Title}]: {mem.Content}");
    }

    if (removedIds.Count > 0)
    {
      sb.AppendLine($"- Removed memory IDs: {string.Join(", ", removedIds)}");
    }

    return (sb.ToString().TrimEnd(), currentSnapshot);
  }
}
