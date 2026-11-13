using Microsoft.EntityFrameworkCore.Metadata;
using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual async Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, CancellationToken ct = default)
  {
    if (entity == null)
    {
      return;
    }

    var elements = await EnsureElementsAsync(ct);
    await PopulateReferencesCoreAsync(dict, entity, elements.List, ct);
  }

  public virtual async Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, ScreenDto screen, CancellationToken ct = default)
  {
    if (entity == null || screen.Layout?.Root == null)
    {
      return;
    }

    var formModel = FindFormModel(screen.Layout.Root);
    if (formModel == null)
    {
      return;
    }

    await PopulateReferencesCoreAsync(dict, entity, formModel.Elements, ct);
  }

  private async Task ResolveTimeZoneInheritLabelsAsync(
    Form.Elements elements,
    TEntity? entity,
    CancellationToken ct)
  {
    var tzModels = elements.List.Select(e => e.Model).OfType<FormTimeZoneModel>().ToList();
    if (tzModels.Count == 0)
    {
      return;
    }

    var isTenantForm = typeof(BaseAppTenant).IsAssignableFrom(typeof(TEntity));
    var startTenantId = isTenantForm
      ? entity is BaseAppTenant tenantEntity ? tenantEntity.ParentId : Request.ParentId
      : entity is IHasTenant hasTenant && hasTenant.TenantId > 0
        ? hasTenant.TenantId
        : db.TenantContext.TenantId;
    string? resolvedZone = null;
    if (startTenantId is > 0 && db.SetOfBase<BaseAppTenant>() is { } tenantQuery)
    {
      var currentId = startTenantId;
      var visited = new HashSet<long>();
      while (currentId is > 0 && visited.Add(currentId.Value))
      {
        var row = await tenantQuery.IgnoreQueryFilters()
          .AsNoTracking()
          .Where(t => t.Id == currentId.Value)
          .Select(t => new { t.ParentId, t.TimeZone })
          .FirstOrDefaultAsync(ct);
        if (row == null)
        {
          break;
        }

        if (!string.IsNullOrWhiteSpace(row.TimeZone))
        {
          resolvedZone = row.TimeZone;
          break;
        }

        currentId = row.ParentId;
      }
    }

    var inheritLabel = !string.IsNullOrWhiteSpace(resolvedZone)
      ? (isTenantForm
          ? L["TimeZoneAsParentOrganisation", resolvedZone]
          : L["TimeZoneAsOrganisation", resolvedZone])
      : L["TimeZoneByBrowser"];

    foreach (var tz in tzModels)
    {
      tz.Inherit = inheritLabel;
    }
  }

  private async Task<TEntity?> PopulateInfoValuesAsync(
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, object?> dict,
    object? entity,
    long? entityId,
    CancellationToken ct)
  {
    var lastSavedEntity = State.LastSavedEntity;
    var infoTarget = entity ?? lastSavedEntity;
    if (infoTarget == null)
    {
      return lastSavedEntity;
    }

    var infoElements = elements
      .Where(el => el.InfoValueGetter != null && (el.Model is FormInfoModel || el.Path.Contains('.')))
      .ToList();
    if (infoElements.Count == 0)
    {
      return lastSavedEntity;
    }

    var efModel = db.Model.FindEntityType(typeof(TEntity));
    var allNavPaths = ResolveValidEntityNavigationPaths(
      efModel,
      infoElements
        .Where(el => el.InfoNavigationPaths is { Count: > 0 })
        .SelectMany(el => el.InfoNavigationPaths!));

    if (allNavPaths.Count > 0 && entityId is > 0)
    {
      var targetEntity = lastSavedEntity ?? infoTarget as TEntity;
      var needsNavReload = targetEntity == null;
      if (targetEntity != null)
      {
        foreach (var navPath in allNavPaths)
        {
          var segments = navPath.Split('.');
          object? currentObj = targetEntity;
          for (var i = 0; i < segments.Length; i++)
          {
            var segment = segments[i];
            var navVal = PropertyAccessorCache.GetValue(currentObj, segment);
            var fkVal = PropertyAccessorCache.GetValue(currentObj, segment + "Id") as long?;
            if (fkVal is > 0 && (navVal == null || navVal is IEntity navEntity && navEntity.Id != fkVal.Value))
            {
              needsNavReload = true;
              break;
            }

            if (fkVal is null or <= 0 && navVal != null && PropertyAccessorCache.GetProperty(currentObj.GetType(), segment + "Id") != null)
            {
              PropertyAccessorCache.GetSetter(currentObj.GetType(), segment)?.Invoke(currentObj, null);
              navVal = null;
            }

            if (navVal == null)
            {
              break;
            }

            currentObj = navVal;
          }

          if (needsNavReload)
          {
            break;
          }
        }
      }

      if (needsNavReload)
      {
        var reloadQuery = Reachable(asTracking: false);
        foreach (var navPath in allNavPaths)
        {
          reloadQuery = reloadQuery.Include(navPath);
        }

        var reloaded = await reloadQuery.FirstOrDefaultAsync(e => e.Id == entityId.Value, ct);
        if (reloaded != null)
        {
          lastSavedEntity = reloaded;
          if (ReferenceEquals(infoTarget, targetEntity))
          {
            infoTarget = reloaded;
          }
        }
      }
    }

    foreach (var el in infoElements)
    {
      dict[el.Path] = el.InfoValueGetter!(infoTarget)
        ?? (lastSavedEntity != null && !ReferenceEquals(infoTarget, lastSavedEntity) ? el.InfoValueGetter!(lastSavedEntity) : null);
    }

    return lastSavedEntity;
  }

  private static List<string> ResolveValidEntityNavigationPaths(
    IEntityType? rootEntityType,
    IEnumerable<string> candidatePaths)
  {
    var result = new List<string>();
    if (rootEntityType == null)
    {
      return result;
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var candidate in candidatePaths)
    {
      var currentType = rootEntityType;
      var validSegments = new List<string>();
      foreach (var segment in candidate.Split('.'))
      {
        var nav = currentType.GetNavigations()
          .FirstOrDefault(n => !n.IsCollection && string.Equals(n.Name, segment, StringComparison.OrdinalIgnoreCase));
        if (nav == null)
        {
          break;
        }

        validSegments.Add(nav.Name);
        currentType = nav.TargetEntityType;
      }

      if (validSegments.Count > 0)
      {
        var validPath = string.Join('.', validSegments);
        if (seen.Add(validPath))
        {
          result.Add(validPath);
        }
      }
    }

    return result;
  }

  private async Task PopulateReferencesCoreAsync(
    Dictionary<string, object?> dict,
    object entity,
    IEnumerable<FormElementDto> elements,
    CancellationToken ct)
  {
    var entityType = entity.GetType();
    var elementList = elements as IReadOnlyList<FormElementDto> ?? elements.ToList();

    foreach (var el in elementList)
    {
      if (string.IsNullOrEmpty(el.ReferencePath))
      {
        continue;
      }

      var selectModel = el.Model as FormSelectModel;
      var labelPaths = selectModel?.ReferenceLabelPaths;

      if (selectModel?.IsMultiple == true)
      {
        var keys = TryGetValueIgnoreCase(dict, el.Path, out var multiRaw) ? ExtractLongKeys(multiRaw) : [];

        var refs = new List<Reference>(keys.Count);
        if (keys.Count > 0 && !string.IsNullOrEmpty(selectModel.ReferenceService))
        {
          var refServiceType = el.ReferenceServiceType
            ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
            ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType;
          var refGrid = refServiceType != null
            ? ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, PlatformApp) as Grid.IGridService
            : null;
          var refEntityType = ModuleCatalog.GetServiceEntityType(refServiceType);
          var loadedById = refEntityType != null
            ? await ReferenceCaptionResolver.LoadEntitiesByIdsAsync(db, refEntityType, keys, labelPaths, refGrid, ct)
            : [];

          foreach (var k in keys)
          {
            var itemCaption = loadedById.TryGetValue(k, out var loaded)
              ? ReferenceCaptionResolver.ReadDisplayCaption(loaded, labelPaths, refGrid)
              : null;
            refs.Add(new Reference { Id = k, Name = !string.IsNullOrEmpty(itemCaption) ? itemCaption : $"#{k}" });
          }
        }

        dict[el.ReferencePath] = refs;
        continue;
      }

      var key = TryGetValueIgnoreCase(dict, el.Path, out var rawVal) ? ExtractLongKey(rawVal) : null;
      if (key is null or <= 0)
      {
        dict[el.ReferencePath] = null;
        continue;
      }

      var recordId = (entity as IEntity)?.Id ?? (PropertyAccessorCache.GetValue(entity, "Id") is long eid ? eid : 0);
      var navSource = recordId > 0 && State.LastSavedEntity?.Id == recordId
        ? State.LastSavedEntity
        : recordId > 0 && CurrentEntity?.Id == recordId
          ? CurrentEntity
          : entity;
      var navSourceType = navSource.GetType();
      var navObj = PropertyAccessorCache.GetValue(navSource, el.ReferencePath)
        ?? (!ReferenceEquals(navSource, entity) ? PropertyAccessorCache.GetValue(entity, el.ReferencePath) : null);
      if (navObj is IEntity navEnt && navEnt.Id != key.Value)
      {
        navObj = null;
      }

      if (navObj == null)
      {
        var candidateName = el.NavigationPropertyName
          ?? (el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? el.Path[..^2] : el.Path);
        if (PropertyAccessorCache.GetProperty(navSourceType, candidateName) is not null)
        {
          navObj = PropertyAccessorCache.GetValue(navSource, candidateName);
          if (navObj is IEntity candNavEnt && candNavEnt.Id != key.Value)
          {
            navObj = null;
          }
        }

        if (navObj == null && !ReferenceEquals(navSource, entity) && PropertyAccessorCache.GetProperty(entityType, candidateName) is not null)
        {
          navObj = PropertyAccessorCache.GetValue(entity, candidateName);
          if (navObj is IEntity entityNavEnt && entityNavEnt.Id != key.Value)
          {
            navObj = null;
          }
        }
      }

      var singleRefServiceType = selectModel != null && !string.IsNullOrEmpty(selectModel.ReferenceService)
        ? el.ReferenceServiceType
          ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
          ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType
        : el.ReferenceServiceType;
      var singleRefGrid = singleRefServiceType != null
        ? ModuleCatalog.Instance.CreateServiceInstance(singleRefServiceType, db, PlatformApp) as Grid.IGridService
        : null;

      var refName = ReferenceCaptionResolver.ReadDisplayCaption(navObj, labelPaths, singleRefGrid);

      if (string.IsNullOrEmpty(refName) && singleRefServiceType != null)
      {
        var refEntityType = ModuleCatalog.GetServiceEntityType(singleRefServiceType);
        if (refEntityType != null)
        {
          var loadedById = await ReferenceCaptionResolver.LoadEntitiesByIdsAsync(
            db,
            refEntityType,
            [key.Value],
            labelPaths,
            singleRefGrid,
            ct);
          if (loadedById.TryGetValue(key.Value, out var loaded))
          {
            refName = ReferenceCaptionResolver.ReadDisplayCaption(loaded, labelPaths, singleRefGrid);
          }
        }
      }

      dict[el.ReferencePath] = new Reference
      {
        Id = key.Value,
        Name = !string.IsNullOrEmpty(refName) ? refName : $"#{key.Value}"
      };
    }

    var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elementList)
    {
      if (!string.IsNullOrEmpty(el.Path) && !el.IsSecret && !string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase))
      {
        allowedKeys.Add(el.Path);
      }

      if (!string.IsNullOrEmpty(el.ReferencePath))
      {
        allowedKeys.Add(el.ReferencePath);
      }

      CollectFilterFields(el.VisibleWhen, allowedKeys);
      CollectFilterFields(el.DisabledWhen, allowedKeys);
      if (el.Model is FormSelectModel { ReferenceDraftRules: { Count: > 0 } draftRules })
      {
        foreach (var dr in draftRules)
        {
          if (!string.IsNullOrEmpty(dr.DraftField))
          {
            allowedKeys.Add(dr.DraftField);
          }
        }
      }
    }

    foreach (var prop in PropertyAccessorCache.GetProperties(entityType))
    {
      if (Attribute.IsDefined(prop, typeof(SensitiveAttribute)))
      {
        allowedKeys.Remove(JsonNamingPolicy.CamelCase.ConvertName(prop.Name));
        allowedKeys.Remove(Form.Elements.ToCamelPath(prop.Name));
        allowedKeys.Remove(prop.Name);
      }
    }

    if (allowedKeys.Count > 0)
    {
      foreach (var key in dict.Keys.ToList())
      {
        if (!allowedKeys.Contains(key))
        {
          dict.Remove(key);
        }
      }
    }
  }

  private async Task PopulateJoinedReferencesAsync(
    IReadOnlyList<FormElementDto> elements,
    TEntity entity,
    CancellationToken ct)
  {
    foreach (var el in elements)
    {
      if (el.JoinLoader == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var ids = await el.JoinLoader(db, entity.Id, ct);
      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp != null && entityProp.CanWrite && entityProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
      {
        entityProp.SetValue(entity, new List<long>(ids));
      }
    }
  }

  private async Task SyncJoinedReferencesAsync(
    IReadOnlyList<FormElementDto> elements,
    TEntity entity,
    CancellationToken ct)
  {
    var anyChanged = false;
    foreach (var el in elements)
    {
      if (el.JoinSyncer == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var raw = PropertyAccessorCache.GetValue(entity, el.Path);
      var desired = new List<long>();
      if (raw is IEnumerable<long> longSeq)
      {
        desired = longSeq.Where(id => id > 0).Distinct().ToList();
      }
      else if (raw is IEnumerable enumerable and not string)
      {
        foreach (var item in enumerable)
        {
          if (FormValidator.TryExtractIntegralOrEnumId(item, out var parsed) && parsed > 0 && !desired.Contains(parsed))
          {
            desired.Add(parsed);
          }
          else if (item != null && long.TryParse(item.ToString(), out var strParsed) && strParsed > 0 && !desired.Contains(strParsed))
          {
            desired.Add(strParsed);
          }
        }
      }

      if (await el.JoinSyncer(db, entity.Id, desired, ct))
      {
        anyChanged = true;
      }

      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp != null && entityProp.CanWrite && entityProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
      {
        entityProp.SetValue(entity, new List<long>(desired));
      }
    }

    if (anyChanged)
    {
      await db.SaveChangesAsync(ct);
    }
  }

  private static FormModel? FindFormModel(PanelNodeDto node)
  {
    if (node.Model is FormModel fm)
    {
      return fm;
    }

    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        var res = FindFormModel(child);
        if (res != null)
        {
          return res;
        }
      }
    }

    return null;
  }

  private static bool TryGetValueIgnoreCase(Dictionary<string, object?> dict, string key, out object? value)
  {
    if (dict.TryGetValue(key, out value))
    {
      return true;
    }

    foreach (var kvp in dict)
    {
      if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
      {
        value = kvp.Value;
        return true;
      }
    }

    value = null;
    return false;
  }

  private static void CollectFilterFields(FilterNode? node, HashSet<string> target)
  {
    switch (node)
    {
      case FilterRule rule when !string.IsNullOrEmpty(rule.Field):
        target.Add(rule.Field);
        break;
      case FilterGroup { Items: { Count: > 0 } items }:
        foreach (var child in items)
        {
          CollectFilterFields(child, target);
        }

        break;
    }
  }

  private static List<long> ExtractLongKeys(object? multiRaw)
  {
    var keys = new List<long>();
    if (multiRaw is JsonElement { ValueKind: JsonValueKind.Array } jeArr)
    {
      foreach (var item in jeArr.EnumerateArray())
      {
        if (ExtractLongKey(item) is > 0 and var id)
        {
          keys.Add(id);
        }
      }
    }
    else if (multiRaw is IEnumerable enumerable and not string)
    {
      foreach (var item in enumerable)
      {
        if (ExtractLongKey(item) is > 0 and var id)
        {
          keys.Add(id);
        }
      }
    }

    return keys;
  }

  private static long? ExtractLongKey(object? rawVal) =>
    rawVal switch
    {
      null => null,
      _ when FormValidator.TryExtractIntegralOrEnumId(rawVal, out var integralId) => integralId,
      JsonElement { ValueKind: JsonValueKind.String } je when long.TryParse(je.GetString(), out var jeParsed) => jeParsed,
      _ when long.TryParse(rawVal.ToString(), out var parsedId) => parsedId,
      _ => null
    };
}
