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
        .Panel("bar", p => p.Model(new NavModel
        {
          Key = $"{servicePath}.diff.nav",
          Start = [new NavCloseModel { Key = "form-close", Label = L["Close"], Icon = UiIcon.Close, Color = UiColor.Slate, Variant = ButtonVariant.Soft }],
          End = [new NavCloseModel { Key = "close", Icon = UiIcon.Close, Color = UiColor.Slate, Variant = ButtonVariant.Ghost, Title = L["Close"] }]
        }).Auto().Pinned().Rigid())
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
      var users = await LoadUserNamesAsync([change.UserId.Value], ct);
      userName = users.TryGetValue(change.UserId.Value, out var uName) && !string.IsNullOrWhiteSpace(uName)
        ? uName
        : $"#{change.UserId.Value}";
    }

    var timestampText = change.At.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    var subtitle = !string.IsNullOrEmpty(userName) ? $"{timestampText} · {userName}" : timestampText;

    var opColor = change.Operation switch
    {
      ChangeOperations.Added => UiColor.Green,
      ChangeOperations.Modified => UiColor.Amber,
      ChangeOperations.Deleted => UiColor.Red,
      ChangeOperations.Restored => UiColor.Indigo,
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

      var formattedBefore = await FormatAuditPropertyValueAsync(targetDb, efEntity, modL, key, beforeElem, ct);
      var formattedAfter = await FormatAuditPropertyValueAsync(targetDb, efEntity, modL, key, afterElem, ct);

      if (change.Operation == ChangeOperations.Added && string.IsNullOrEmpty(formattedAfter))
      {
        continue;
      }

      if (change.Operation == ChangeOperations.Deleted && string.IsNullOrEmpty(formattedBefore))
      {
        continue;
      }

      var label = ResolveFieldLabel(targetFeat, modL, entityType, key);
      var isChanged = change.Operation is ChangeOperations.Modified or ChangeOperations.Restored &&
                      !string.Equals(formattedBefore, formattedAfter, StringComparison.Ordinal);

      fields.Add(new ChangeDiffFieldDto
      {
        Key = key,
        Label = label,
        Before = formattedBefore,
        After = formattedAfter,
        IsChanged = isChanged
      });
    }

    var canRestore = CanRestoreTable(change.Table) &&
                     change.Operation is ChangeOperations.Modified or ChangeOperations.Deleted &&
                     !string.IsNullOrEmpty(change.Before) &&
                     entityType != null &&
                     !typeof(BaseAppFile).IsAssignableFrom(entityType);

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

    var diffModel = new ChangeDiffModel
    {
      Id = change.Id,
      Title = title,
      Subtitle = subtitle,
      Operation = L[change.Operation],
      OperationKey = change.Operation,
      OperationColor = opColor,
      Service = servicePath,
      ParentService = ParentService,
      ParentId = ParentId,
      CanRestore = canRestore,
      RestoreLabel = L["Restore"],
      RestoreConfirm = L["RestoreConfirm"],
      RestoredLabel = L["Restored"],
      EmptyLabel = L["ChangeDiffEmpty"],
      Fields = fields
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

    if (feat != null)
    {
      return modL.TextOr($"{feat.CleanName}.{camel}",
        modL.TextOr($"{feat.Label}.{camel}",
          modL.TextOr($"{feat.Name}.{camel}",
            modL.TextOr(propName, modL[spaced]))));
    }

    return modL.TextOr(propName, modL[spaced]);
  }

  private async Task<string?> FormatAuditPropertyValueAsync(
    PlatformDbContext targetDb,
    Microsoft.EntityFrameworkCore.Metadata.IEntityType? efEntity,
    Localizer modL,
    string propName,
    JsonElement elem,
    CancellationToken ct)
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
      if (principalType != null)
      {
        try
        {
          var principalDb = targetDb.Model.FindEntityType(principalType) != null ? targetDb : GetDbContextForTable(principalType.Name);
          var refEntity = await principalDb.FindAsync(principalType, [fkId], ct);
          if (refEntity != null)
          {
            var rawName = (PropertyAccessorCache.GetValue(refEntity, "Name")
              ?? PropertyAccessorCache.GetValue(refEntity, "Title")
              ?? PropertyAccessorCache.GetValue(refEntity, "Number")
              ?? PropertyAccessorCache.GetValue(refEntity, "Code")
              ?? PropertyAccessorCache.GetValue(refEntity, "Login"))?.ToString();
            if (!string.IsNullOrWhiteSpace(rawName))
            {
              return refEntity is IReadOnlyEntity ? modL[rawName] : rawName;
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
        return propName.EndsWith("Date", StringComparison.Ordinal) && dto.TimeOfDay == TimeSpan.Zero
          ? dto.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
          : dto.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
      }

      return str;
    }

    return elem.GetRawText();
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
}
