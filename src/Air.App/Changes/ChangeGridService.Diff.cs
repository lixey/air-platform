using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Changes;

public abstract partial class ChangeGridService
{
  private static readonly JsonSerializerOptions UnescapedJsonOptions = new()
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) =>
    request?.Id is > 0
      ? BuildDiffScreenAsync(servicePath, request, ct)
      : base.BuildScreenAsync(servicePath, request, ct);

  public virtual async Task<ScreenDto> BuildDiffScreenAsync(string servicePath, GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    var change = request.Id is > 0
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

    var targetDb = GetDbContextForTable(change.Table);
    var efEntity = targetDb.Model.GetEntityTypes()
      .FirstOrDefault(e => e.ClrType.Name == change.Table || e.GetTableName() == change.Table);
    var entityType = efEntity?.ClrType;
    var targetFeat = ModuleCatalog.Instance.FindFeatureForEntity(entityType, change.Table);
    var modL = targetFeat != null && !string.IsNullOrEmpty(targetFeat.Module)
      ? Localizer.Instance.For(targetFeat.Module)
      : L;

    var entityLabel = targetFeat != null
      ? modL.TextOr(targetFeat.Label, modL.TextOr(targetFeat.CleanName, change.Table))
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

    var beforeDict = ParseJsonDictionary(change.Before);
    var afterDict = ParseJsonDictionary(change.After);

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
      .OrderBy(k => k is "CreatedAt" or "UpdatedAt" ? 1 : 0)
      .ToList();

    var fields = new List<ChangeDiffFieldDto>(orderedKeys.Count);
    foreach (var key in orderedKeys)
    {
      beforeDict.TryGetValue(key, out var beforeElem);
      afterDict.TryGetValue(key, out var afterElem);

      var formattedBefore = await FormatAuditPropertyValueAsync(
        targetDb,
        efEntity,
        modL,
        key,
        beforeElem,
        ct
      );
      var formattedAfter = await FormatAuditPropertyValueAsync(
        targetDb,
        efEntity,
        modL,
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

      var label = ResolveFieldLabel(targetFeat, modL, entityType, key);
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

    var (sourceBadge, sourceBadgeColor) = ResolveSourceBadge(change.CorrelationId, L);
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

  private static Dictionary<string, JsonElement> ParseJsonDictionary(string? json)
  {
    var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(json))
    {
      return dict;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      if (doc.RootElement.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
          dict[prop.Name] = prop.Value.Clone();
        }
      }
    }
    catch { }

    return dict;
  }

  private static string ResolveFieldLabel(Feature? feat, Localizer modL, Type? entityType, string propName)
  {
    var camel = char.ToLowerInvariant(propName[0]) + propName[1..];
    var spaced = Columns.PathToLabel(camel, entityType);
    if (spaced.EndsWith(" Id", StringComparison.Ordinal) && spaced.Length > 3)
    {
      spaced = spaced[..^3];
    }

    var fallback = modL.TextOr(propName, modL[spaced]);
    return feat != null
      ? ScreenBuilderHelpers.ResolveFieldLabel(modL, feat, camel, fallback)
      : fallback;
  }

  private async Task<string?> FormatAuditPropertyValueAsync(
    PlatformDbContext targetDb,
    Microsoft.EntityFrameworkCore.Metadata.IEntityType? efEntity,
    Localizer modL,
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
        try
        {
          var principalDb = targetDb.Model.FindEntityType(principalType) != null ? targetDb : GetDbContextForTable(principalType.Name);
          var pkVal = pkType == typeof(long)
            ? (object)fkId
            : Convert.ChangeType(fkId, Nullable.GetUnderlyingType(pkType) ?? pkType, CultureInfo.InvariantCulture);
          var refEntity = await principalDb.FindAsync(principalType, [pkVal], ct);
          if (refEntity != null)
          {
            var rawName = PropertyAccessorCache.GetReferenceLabel(refEntity);
            if (!string.IsNullOrWhiteSpace(rawName))
            {
              return rawName;
            }
          }
        }
        catch { }
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

  private static string? FormatCompactAuditJson(string? targetJson, string? compareJson, bool onlyDiff)
  {
    if (string.IsNullOrWhiteSpace(targetJson))
    {
      return targetJson;
    }

    try
    {
      using var targetDoc = JsonDocument.Parse(targetJson);
      if (targetDoc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return UnescapeJsonText(targetJson);
      }

      JsonDocument? compareDoc = null;
      if (onlyDiff && !string.IsNullOrWhiteSpace(compareJson))
      {
        try
        {
          compareDoc = JsonDocument.Parse(compareJson);
        }
        catch { }
      }

      using (compareDoc)
      {
        var hasCompareObj = compareDoc?.RootElement.ValueKind == JsonValueKind.Object;
        var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var prop in targetDoc.RootElement.EnumerateObject())
        {
          if (prop.Name is "Id" or "TenantId" or "Ver" or "CreatedAt" or "UpdatedAt")
          {
            continue;
          }

          if (onlyDiff && hasCompareObj)
          {
            if (compareDoc!.RootElement.TryGetProperty(prop.Name, out var otherVal) &&
                string.Equals(prop.Value.GetRawText(), otherVal.GetRawText(), StringComparison.Ordinal))
            {
              continue;
            }
          }
          else if (!onlyDiff && prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
          {
            continue;
          }

          filtered[prop.Name] = prop.Value;
        }

        if (filtered.Count > 0)
        {
          return JsonSerializer.Serialize(filtered, UnescapedJsonOptions);
        }
      }
    }
    catch { }

    return UnescapeJsonText(targetJson);
  }

  private static string? UnescapeJsonText(string? json)
  {
    if (string.IsNullOrEmpty(json) || !json.Contains("\\u", StringComparison.OrdinalIgnoreCase))
    {
      return json;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      return JsonSerializer.Serialize(doc.RootElement, UnescapedJsonOptions);
    }
    catch
    {
      return Regex.Unescape(json);
    }
  }

  private static string FormatCorrelationLabel(string correlationId, Localizer loc)
  {
    if (TryParseAgentCorrelation(correlationId, out var code, out var batchCount))
    {
      return batchCount is > 1
        ? loc["ChangeAgentBatchBadge", batchCount.Value, code]
        : loc["ChangeAgentSingleBadge", code];
    }

    return correlationId;
  }

  private static (string? Badge, string? Color) ResolveSourceBadge(string? correlationId, Localizer loc)
  {
    if (string.IsNullOrWhiteSpace(correlationId) || !TryParseAgentCorrelation(correlationId, out var code, out var batchCount))
    {
      return (null, null);
    }

    return batchCount is > 1
      ? (loc["ChangeAgentBatchBadge", batchCount.Value, code], UiColor.Violet)
      : (loc["ChangeAgentSingleBadge", code], UiColor.Blue);
  }

  private static bool TryParseAgentCorrelation(string correlationId, out string code, out int? batchCount)
  {
    code = string.Empty;
    batchCount = null;

    if (correlationId.StartsWith("agent-batch:", StringComparison.OrdinalIgnoreCase))
    {
      var rest = correlationId["agent-batch:".Length..];
      var sep = rest.IndexOf(':');
      if (sep > 0)
      {
        code = rest[..sep];
        if (int.TryParse(rest[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
          batchCount = parsed;
        }
      }
      else
      {
        code = rest;
      }

      return !string.IsNullOrWhiteSpace(code);
    }

    if (correlationId.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
    {
      code = correlationId["agent:".Length..];
      return !string.IsNullOrWhiteSpace(code);
    }

    return false;
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

    foreach (var sib in siblings)
    {
      var sibDb = GetDbContextForTable(sib.Table);
      var sibEf = sibDb.Model.GetEntityTypes()
        .FirstOrDefault(e => e.ClrType.Name == sib.Table || e.GetTableName() == sib.Table);
      var sibClr = sibEf?.ClrType;
      var sibFeat = ModuleCatalog.Instance.FindFeatureForEntity(sibClr, sib.Table);

      if (!hasGlobalView && sib.Id != current.Id)
      {
        var canSeeSibling = sibFeat != null &&
                            (ServiceEndpointsExtensions.CanView(sibFeat, User) ||
                             sibFeat.HasHistory && User != null && (User.Can(sibFeat.History) || User.Can(sibFeat.All) || User.Can("*")));
        if (!canSeeSibling)
        {
          continue;
        }
      }

      var sibModL = sibFeat != null && !string.IsNullOrEmpty(sibFeat.Module)
        ? Localizer.Instance.For(sibFeat.Module)
        : loc;
      var sibEntityLabel = sibFeat != null
        ? sibModL.TextOr(sibFeat.Label, sibModL.TextOr(sibFeat.CleanName, sib.Table))
        : loc[sib.Table];

      var summary = TryExtractRecordSummary(sib.After ?? sib.Before);
      var itemTitle = sib.RowId > 0
        ? (!string.IsNullOrEmpty(summary) ? $"{sibEntityLabel} #{sib.RowId} · {summary}" : $"{sibEntityLabel} #{sib.RowId}")
        : (!string.IsNullOrEmpty(summary) ? $"{sibEntityLabel} · {summary}" : sibEntityLabel);

      var sibOpColor = sib.Operation switch
      {
        ChangeOperations.Added => UiColor.Green,
        ChangeOperations.Modified => UiColor.Amber,
        ChangeOperations.Deleted => UiColor.Red,
        _ => UiColor.Slate
      };

      string? formService = null;
      OverlayDto? overlay = null;
      if (sib.Operation != ChangeOperations.Deleted &&
          sib.RowId > 0 &&
          sibFeat != null &&
          ServiceEndpointsExtensions.CanView(sibFeat, User))
      {
        var formServiceType = ResolvePrimaryFormServiceType(sibFeat, sibClr);
        if (formServiceType != null)
        {
          formService = ServicePathResolver.Resolve(formServiceType);
          var sp = services ?? PlatformApp?.Services;
          var formInst = sp?.GetService(formServiceType) as IFormService
                         ?? ModuleCatalog.Instance.CreateServiceInstance(formServiceType, sibDb, PlatformApp, sp) as IFormService;
          if (formInst != null)
          {
            overlay = await formInst.OverlayAsync(formService, ct);
          }
        }
      }

      items.Add(
        new ChangeDiffRelatedItemDto
        {
          Id = sib.Id,
          Title = itemTitle,
          Operation = loc[sib.Operation],
          OperationColor = sibOpColor,
          FormService = formService,
          Overlay = overlay,
          RecordId = sib.RowId,
          IsCurrent = sib.Id == current.Id
        }
      );
    }

    return items;
  }

  private static string? TryExtractRecordSummary(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      return null;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      if (doc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return null;
      }

      foreach (var key in new[] { "Name", "Title", "Code", "Login", "Note" })
      {
        if (doc.RootElement.TryGetProperty(key, out var prop) &&
            prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { Length: > 0 } val)
        {
          var trimmed = val.Trim();
          return trimmed.Length > 48 ? $"{trimmed[..45]}…" : trimmed;
        }
      }
    }
    catch { }

    return null;
  }

  private static Type? ResolvePrimaryFormServiceType(Feature feat, Type? entityType)
  {
    if (feat.ServiceType != null && typeof(IFormService).IsAssignableFrom(feat.ServiceType))
    {
      return feat.ServiceType;
    }

    return ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
             typeof(IFormService).IsAssignableFrom(t) &&
             entityType != null &&
             ModuleCatalog.GetServiceEntityType(t) == entityType &&
             string.Equals(t.Name, $"{feat.Name}FormService", StringComparison.OrdinalIgnoreCase)
           )
           ?? ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
             typeof(IFormService).IsAssignableFrom(t) &&
             entityType != null &&
             ModuleCatalog.GetServiceEntityType(t) == entityType
           );
  }
}
