using Microsoft.EntityFrameworkCore.Metadata;
using Platform.App.Changes.Internals;
using Platform.App.Internals;

namespace Platform.App.Changes;

public abstract partial class ChangeGridService
{
  private const string OverlayCacheKey = "ChangeGrid.OverlayCache";
  private const string FkLabelCacheKey = "ChangeGrid.FkLabelCache";

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) =>
    request?.Id is > 0
      ? BuildDiffScreenAsync(servicePath, request, ct)
      : base.BuildScreenAsync(servicePath, request, ct);

  public virtual async Task<ScreenDto> BuildDiffScreenAsync(string servicePath, GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    var parentScopeValid = await EnsureParentScopeAsync(lockParent: false, ct);
    var change = parentScopeValid && request.Id is > 0
      ? await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id.Value, ct)
      : null;

    if (change == null)
    {
      var emptyLayout = PanelLayout.Column($"{servicePath}.diff")
        .Panel(
          "bar",
          p => p.Model(
            new NavModel
            {
              Key = $"{servicePath}.diff.nav",
              Start =
              [
                new NavCloseModel
                  { Key = "form-close", Label = L["Close"], Icon = UiIcon.Close, Color = UiColor.Slate, Variant = ButtonVariant.Soft }
              ],
              End =
              [
                new NavCloseModel
                  { Key = "close", Icon = UiIcon.Close, Color = UiColor.Slate, Variant = ButtonVariant.Ghost, Title = L["Close"] }
              ]
            }
          ).Auto().Pinned().Rigid()
        )
        .Build();
      return new ScreenDto { Layout = emptyLayout, OverlayWidth = PanelSize.Px(560) };
    }

    var (targetDb, efEntity) = ResolveTableMetadata(change.Table);
    var entityType = efEntity?.ClrType;
    var targetFeat = (!string.IsNullOrEmpty(ParentService) ? Catalog.FindFeature(ParentService) : null)
      ?? Catalog.FindFeatureForEntity(entityType, change.Table);
    var modL = targetFeat != null && !string.IsNullOrEmpty(targetFeat.Module)
      ? Localizer.Instance.For(targetFeat.Module)
      : L;

    var entityLabel = targetFeat != null
      ? modL.TextOr(targetFeat.Label, modL.TextOr(targetFeat.Handle, change.Table))
      : L[change.Table];
    var title = $"{entityLabel} #{change.RowId}";

    string? userName = null;
    if (change.UserId is > 0)
    {
      var users = await db.LoadUserNamesAsync([change.UserId.Value], ct);
      userName = users.TryGetValue(change.UserId.Value, out var uName) && !string.IsNullOrWhiteSpace(uName)
        ? uName
        : $"#{change.UserId.Value}";
    }

    TimeZones.TryFind(User?.TimeZone, out var userTimeZone);
    var timestampText = TimeZoneInfo.ConvertTime(change.At, userTimeZone).ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    var subtitle = !string.IsNullOrEmpty(userName) ? $"{timestampText} · {userName}" : timestampText;

    var opColor = change.Operation switch
    {
      ChangeOperations.Added => UiColor.Green,
      ChangeOperations.Modified => UiColor.Amber,
      ChangeOperations.Deleted => UiColor.Red,
      _ => UiColor.Slate
    };

    var beforeDict = ChangeAuditFormatter.ParseJsonDictionary(change.Before);
    var afterDict = ChangeAuditFormatter.ParseJsonDictionary(change.After);

    var allKeys = new List<string>();
    foreach (var k in beforeDict.Keys.Concat(afterDict.Keys))
    {
      if (k is "Id" or "TenantId" or "Ver" || allKeys.Contains(k, StringComparer.Ordinal))
      {
        continue;
      }

      allKeys.Add(k);
    }

    var orderedKeys = allKeys
      .OrderBy(static k => k is "CreatedAt" or "UpdatedAt" ? 1 : 0)
      .ToList();

    var fields = new List<ChangeDiffFieldDto>(orderedKeys.Count);
    foreach (var key in orderedKeys)
    {
      var hasBefore = beforeDict.TryGetValue(key, out var beforeElem);
      var hasAfter = afterDict.TryGetValue(key, out var afterElem);

      var formattedBefore = await FormatAuditPropertyValueAsync(
        targetDb,
        efEntity,
        key,
        beforeElem,
        ct
      );
      var formattedAfter = hasBefore && hasAfter && string.Equals(beforeElem.GetRawText(), afterElem.GetRawText(), StringComparison.Ordinal)
        ? formattedBefore
        : await FormatAuditPropertyValueAsync(
            targetDb,
            efEntity,
            key,
            afterElem,
            ct
          );

      if (change.Operation == ChangeOperations.Added && string.IsNullOrEmpty(formattedAfter))
      {
        continue;
      }

      if (change.Operation == ChangeOperations.Deleted && string.IsNullOrEmpty(formattedBefore))
      {
        continue;
      }

      var label = ChangeAuditFormatter.ResolveFieldLabel(targetFeat, modL, entityType, key);
      var isChanged = change.Operation == ChangeOperations.Modified &&
                      !string.Equals(formattedBefore, formattedAfter, StringComparison.Ordinal);

      fields.Add(
        new ChangeDiffFieldDto
        {
          Key = key,
          Label = label,
          Before = formattedBefore,
          After = formattedAfter,
          IsChanged = isChanged
        }
      );
    }

    var navModel = new NavModel
    {
      Key = $"{servicePath}.diff.nav",
      Start =
      [
        new NavCloseModel
        {
          Key = "form-close",
          Label = L["Close"],
          Icon = UiIcon.Close,
          Color = UiColor.Slate,
          Variant = ButtonVariant.Soft
        }
      ],
      Elements =
      [
        new NavLabelModel
        {
          Key = "record",
          Label = $"#{change.Id} · {title}",
          Color = UiColor.Sky
        }
      ],
      End =
      [
        new NavCloseModel
        {
          Key = "close",
          Icon = UiIcon.Close,
          Color = UiColor.Slate,
          Variant = ButtonVariant.Ghost,
          Title = L["Close"]
        }
      ]
    };

    var (sourceBadge, sourceBadgeColor) = ChangeAuditFormatter.ResolveSourceBadge(change.CorrelationId, L);
    var relatedChanges = await BuildRelatedChangesAsync(change, L, ct);
    if (sourceBadge == null && relatedChanges.Count > 1)
    {
      sourceBadge = L["ChangePackBadge", relatedChanges.Count];
      sourceBadgeColor = UiColor.Sky;
    }

    var diffModel = new ChangeDiffModel
    {
      Id = change.Id,
      Title = title,
      Subtitle = subtitle,
      Operation = L[change.Operation],
      OperationKey = change.Operation,
      OperationColor = opColor,
      EmptyLabel = L["ChangeDiffEmpty"],
      SourceBadge = sourceBadge,
      SourceBadgeColor = sourceBadgeColor,
      RelatedTitle = relatedChanges.Count > 1 ? L["ChangeRelatedTitle", relatedChanges.Count] : null,
      Fields = fields,
      RelatedChanges = relatedChanges.Count > 1 ? relatedChanges : []
    };

    var layout = PanelLayout.Column($"{servicePath}.diff")
      .Panel("bar", p => p.Model(navModel).Auto().Pinned().Rigid())
      .Panel("diff", p => p.Model(diffModel).Auto())
      .Build();

    return new ScreenDto
    {
      Layout = layout,
      OverlayWidth = PanelSize.Px(560)
    };
  }

  private async Task<string?> FormatAuditPropertyValueAsync(
    PlatformDbContext targetDb,
    IEntityType? efEntity,
    string propName,
    JsonElement elem,
    CancellationToken ct
  )
  {
    if (elem.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
    {
      return null;
    }

    if (elem.ValueKind == JsonValueKind.True)
    {
      return L["Yes"];
    }

    if (elem.ValueKind == JsonValueKind.False)
    {
      return L["No"];
    }

    if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out var fkId) && fkId > 0)
    {
      var efProp = efEntity?.FindProperty(propName);
      var fk = efProp?.GetContainingForeignKeys().FirstOrDefault();
      var principalType = fk?.PrincipalEntityType.ClrType;
      if (principalType != null && fk?.PrincipalKey.Properties is [{ ClrType: var pkType }])
      {
        var labelCache = GetExecutionItem<Dictionary<(Type, long), string?>>(FkLabelCacheKey);
        if (labelCache is null)
        {
          labelCache = [];
          SetExecutionItem(FkLabelCacheKey, labelCache);
        }

        if (!labelCache.TryGetValue((principalType, fkId), out var resolvedName))
        {
          try
          {
            var principalDb = targetDb.Model.FindEntityType(principalType) != null
              ? targetDb
              : ResolveTableMetadata(principalType.Name).Db;
            var pkVal = pkType == typeof(long)
              ? (object)fkId
              : Convert.ChangeType(fkId, Nullable.GetUnderlyingType(pkType) ?? pkType, CultureInfo.InvariantCulture);
            var refEntity = await principalDb.FindAsync(principalType, [pkVal], ct);
            if (refEntity != null)
            {
              var rawName = PropertyAccessorCache.GetReferenceLabel(refEntity);
              if (!string.IsNullOrWhiteSpace(rawName))
              {
                resolvedName = rawName;
              }
            }
          }
          catch
          {
          }

          labelCache[(principalType, fkId)] = resolvedName;
        }

        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
          return resolvedName;
        }
      }
    }

    if (elem.ValueKind == JsonValueKind.String)
    {
      var str = elem.GetString();
      if (string.IsNullOrEmpty(str))
      {
        return null;
      }

      if ((propName.EndsWith("At", StringComparison.Ordinal) || propName.Contains("Date", StringComparison.Ordinal)) &&
          DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
      {
        if (propName.EndsWith("Date", StringComparison.Ordinal) && dto.TimeOfDay == TimeSpan.Zero)
        {
          return dto.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        TimeZones.TryFind(User?.TimeZone, out var userTimeZone);
        return TimeZoneInfo.ConvertTime(dto, userTimeZone).ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
      }

      return str;
    }

    return elem.GetRawText();
  }

  private async Task<List<ChangeDiffRelatedItemDto>> BuildRelatedChangesAsync(
    AppPlatformChange current,
    Localizer loc,
    CancellationToken ct
  )
  {
    if (current.RunId == Guid.Empty)
    {
      return [];
    }

    var siblings = await db.AppPlatformChange
      .AsNoTracking()
      .Where(c => c.TenantId == current.TenantId && c.RunId == current.RunId)
      .OrderBy(c => c.Id)
      .Take(50)
      .ToListAsync(ct);

    if (siblings.Count <= 1)
    {
      return [];
    }

    var hasGlobalView = base.CanView(new GridRequest(), User);
    var items = new List<ChangeDiffRelatedItemDto>(siblings.Count);
    var defaultFeatureByTable = new Dictionary<string, Feature?>(StringComparer.Ordinal);

    foreach (var sib in siblings)
    {
      var (sibDb, sibEf) = ResolveTableMetadata(sib.Table);
      var sibClr = sibEf?.ClrType;
      var sibFeat = !string.IsNullOrEmpty(ParentService) && sib.Table == current.Table && sib.RowId == current.RowId
        ? Catalog.FindFeature(ParentService) ?? ResolveDefaultFeatureForTable(sib.Table, sibClr, defaultFeatureByTable)
        : ResolveDefaultFeatureForTable(sib.Table, sibClr, defaultFeatureByTable);
      if (!hasGlobalView && sib.Id != current.Id)
      {
        var (canSeeSibling, resolvedSibFeat) = await ResolvePermittedSiblingFeatureAsync(sibDb, sibClr, sib.Table, sib.RowId, sibFeat, ct);
        if (!canSeeSibling)
        {
          continue;
        }

        sibFeat = resolvedSibFeat ?? sibFeat;
      }

      var sibModL = sibFeat != null && !string.IsNullOrEmpty(sibFeat.Module)
        ? Localizer.Instance.For(sibFeat.Module)
        : loc;
      var sibEntityLabel = sibFeat != null
        ? sibModL.TextOr(sibFeat.Label, sibModL.TextOr(sibFeat.Handle, sib.Table))
        : loc[sib.Table];

      var summary = ChangeAuditFormatter.TryExtractRecordSummary(sib.After ?? sib.Before);
      var baseTitle = sib.RowId > 0 ? $"{sibEntityLabel} #{sib.RowId}" : sibEntityLabel;
      var itemTitle = !string.IsNullOrEmpty(summary) ? $"{baseTitle} · {summary}" : baseTitle;
      var sibOpColor = sib.Operation switch
      {
        ChangeOperations.Added => UiColor.Green,
        ChangeOperations.Modified => UiColor.Amber,
        ChangeOperations.Deleted => UiColor.Red,
        _ => UiColor.Slate
      };

      OpenTargetDto? open = null;
      if (sib.Operation != ChangeOperations.Deleted &&
          sib.RowId > 0 &&
          sibFeat != null &&
          ServiceEndpointsExtensions.CanView(sibFeat, User))
      {
        var (formService, overlay) = await ResolveFormOverlayForFeatureAsync(sibFeat, sibClr, sibDb, ct);
        if (!string.IsNullOrEmpty(formService))
        {
          open = new OpenTargetDto
          {
            Service = formService,
            Id = sib.RowId,
            Overlay = overlay
          };
        }
      }

      items.Add(
        new ChangeDiffRelatedItemDto
        {
          Id = sib.Id,
          Title = itemTitle,
          Operation = loc[sib.Operation],
          OperationColor = sibOpColor,
          Open = open,
          IsCurrent = sib.Id == current.Id
        }
      );
    }

    return items;
  }

  private Feature? ResolveDefaultFeatureForTable(
    string table,
    Type? clrType,
    Dictionary<string, Feature?> cache)
  {
    if (!cache.TryGetValue(table, out var feat))
    {
      feat = Catalog.FindFeatureForEntity(clrType, table);
      cache[table] = feat;
    }

    return feat;
  }

  private async Task<(string? Service, OverlayDto? Overlay)> ResolveFormOverlayForFeatureAsync(
    Feature feat,
    Type? clrType,
    PlatformDbContext targetDb,
    CancellationToken ct)
  {
    var cache = GetExecutionItem<Dictionary<(Feature, Type?), (string? Service, OverlayDto? Overlay)>>(OverlayCacheKey);
    if (cache is null)
    {
      cache = [];
      SetExecutionItem(OverlayCacheKey, cache);
    }

    var cacheKey = (feat, clrType);
    if (cache.TryGetValue(cacheKey, out var cached))
    {
      return cached;
    }

    var formServiceType = ResolvePrimaryFormServiceType(Catalog, feat, clrType);
    if (formServiceType == null)
    {
      cache[cacheKey] = default;
      return default;
    }

    var formService = ServicePathResolver.Resolve(formServiceType);
    OverlayDto? overlay = null;
    var sp = Services;
    var formInst = sp?.GetService(formServiceType) as IFormService
                   ?? Catalog.CreateServiceInstance(formServiceType, targetDb, PlatformApp, sp) as IFormService;
    if (formInst != null)
    {
      formInst.BindContext(PlatformApp);
      overlay = await formInst.OverlayAsync(formService, ct);
    }

    var resolved = ((string?)formService, overlay);
    cache[cacheKey] = resolved;
    return resolved;
  }

  private async Task<(bool Allowed, Feature? ResolvedFeature)> ResolvePermittedSiblingFeatureAsync(
    PlatformDbContext sibDb,
    Type? sibClr,
    string sibTable,
    long sibRowId,
    Feature? defaultFeat,
    CancellationToken ct)
  {
    bool CanSeeFeature(Feature? feat) =>
      feat != null &&
      ServiceEndpointsExtensions.CanView(feat, User) &&
      feat.HasHistory &&
      User != null &&
      (User.Can(feat.History) || User.Can(feat.All) || User.Can("*"));

    var candidateFeatures = Catalog.AllFeatures
      .Where(f =>
        sibClr != null && f.ServiceType != null && ModuleCatalog.GetServiceEntityType(f.ServiceType) == sibClr ||
        string.Equals(f.Handle, sibTable, StringComparison.OrdinalIgnoreCase))
      .Distinct()
      .ToList();

    foreach (var st in Catalog.AllServiceTypes)
    {
      if (sibClr != null &&
          ModuleCatalog.GetServiceEntityType(st) == sibClr &&
          Catalog.FindFeature(st) is { } svcFeat &&
          !candidateFeatures.Contains(svcFeat))
      {
        candidateFeatures.Add(svcFeat);
      }
    }

    if (candidateFeatures.Count <= 1)
    {
      var singleFeat = candidateFeatures.Count == 1 ? candidateFeatures[0] : defaultFeat;
      return (CanSeeFeature(singleFeat), singleFeat);
    }

    if (sibClr == null || sibRowId <= 0)
    {
      return (candidateFeatures.All(CanSeeFeature), defaultFeat);
    }

    var sp = Services;
    foreach (var feat in candidateFeatures)
    {
      if (!CanSeeFeature(feat))
      {
        continue;
      }

      var formType = ResolvePrimaryFormServiceType(Catalog, feat, sibClr);
      if (formType != null &&
          (sp?.GetService(formType) as IFormService
           ?? Catalog.CreateServiceInstance(formType, sibDb, PlatformApp, sp) as IFormService) is { } form)
      {
        form.BindContext(PlatformApp);
        if (await form.LoadParentRecordAsync(sibRowId, ct) != null)
        {
          return (true, feat);
        }
      }
    }

    return (false, null);
  }

  private static Type? ResolvePrimaryFormServiceType(ModuleCatalog catalog, Feature feat, Type? entityType)
  {
    if (feat.ServiceType != null && !feat.ServiceType.IsAbstract && typeof(IFormService).IsAssignableFrom(feat.ServiceType))
    {
      return feat.ServiceType;
    }

    return catalog.AllServiceTypes.FirstOrDefault(t =>
             !t.IsAbstract &&
             typeof(IFormService).IsAssignableFrom(t) &&
             ReferenceEquals(catalog.FindFeature(t), feat)
           )
           ?? catalog.AllServiceTypes.FirstOrDefault(t =>
             !t.IsAbstract &&
             typeof(IFormService).IsAssignableFrom(t) &&
             entityType != null &&
             ModuleCatalog.GetServiceEntityType(t) == entityType &&
             catalog.FindFeature(t) == null &&
             (string.Equals(t.Name, $"{feat.Module}{feat.Handle}FormService", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(t.Name, $"{feat.Handle}FormService", StringComparison.OrdinalIgnoreCase))
           );
  }
}
