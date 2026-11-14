using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormSelectPatcher
{
  private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = ReferenceHandler.IgnoreCycles
  };

  public static async Task ApplyDraftSelectPatchesAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    IReadOnlyList<FormElementDto> editableElements,
    IReadOnlyList<FormElementDto> targetElements,
    Dictionary<string, object?> originalValues,
    Dictionary<string, object?> values,
    HashSet<string> explicitlyProvided,
    ISet<string>? pendingReferencePaths,
    bool isEdit,
    string? triggerField,
    CancellationToken ct)
  {
    var hasTriggerField = !string.IsNullOrWhiteSpace(triggerField);
    foreach (var el in editableElements)
    {
      if (hasTriggerField)
      {
        if (!string.Equals(el.Path, triggerField, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }
      }
      else if (isEdit && !explicitlyProvided.Contains(el.Path))
      {
        continue;
      }

      if (el.SelectPatches is not { Count: > 0 } patches ||
          el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } ||
          !values.TryGetValue(el.Path, out var curVal) ||
          !FilterNodeEvaluator.TryConvertToDecimal(curVal, out var selectedDec) ||
          selectedDec <= 0)
      {
        continue;
      }

      var selectedId = (long)selectedDec;
      if (isEdit &&
          !hasTriggerField &&
          originalValues.TryGetValue(el.Path, out var oldSrcVal) &&
          FilterNodeEvaluator.TryConvertToDecimal(oldSrcVal, out var oldSrcDec) &&
          (long)oldSrcDec == selectedId)
      {
        continue;
      }

      if (await TryReadReferenceRowAsync(db, app, FormElementBinding.TryGet(el)?.ReferenceServiceType, refSvc, selectedId, ct) is not { } selectedRow)
      {
        continue;
      }

      foreach (var patch in patches)
      {
        var targetEl = targetElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
        if (!hasTriggerField && targetEl is not { IsDisabled: true } && explicitlyProvided.Contains(patch.TargetField) ||
            pendingReferencePaths?.Contains(patch.TargetField) == true)
        {
          continue;
        }

        if ((!isEdit && !hasTriggerField && targetEl is not { IsDisabled: true } || patch.OnlyWhenEmpty) &&
            values.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            !(FilterNodeEvaluator.TryConvertToDecimal(existingTarget, out var existingDec) && existingDec == 0m) &&
            existingTarget.ToString() is { Length: > 0 } existingStr &&
            existingStr != "0")
        {
          continue;
        }

        if (!TryGetRowEntry(selectedRow, patch.SourceColumn, out var patchVal) || patchVal == null)
        {
          continue;
        }

        var isReferenceTarget = targetEl?.ValueKind == FormValueKind.Reference || targetEl?.Model is FormSelectModel;
        if (isReferenceTarget)
        {
          var allowZeroOption = targetEl?.Model is FormSelectModel { Options: { Count: > 0 } opts } && opts.Any(static o => o.Id == 0);
          var patchId = ExtractPatchOptionOrReferenceId(patchVal, allowZeroOption);
          if (patchId is null || !allowZeroOption && patchId <= 0 ||
              !await MatchesReferenceTargetFilterAsync(db, app, targetEl, patchId.Value, ct))
          {
            continue;
          }

          values[patch.TargetField] = patchId.Value;
          continue;
        }

        if (FilterNodeEvaluator.TryConvertToDecimal(patchVal, out var decVal))
        {
          values[patch.TargetField] = patchVal is int or long ? patchVal : decVal;
        }
        else if (patchVal is bool bVal)
        {
          values[patch.TargetField] = bVal;
        }
        else if (patchVal is string sVal && !string.IsNullOrWhiteSpace(sVal))
        {
          values[patch.TargetField] = sVal;
        }
        else if (patchVal is JsonElement je)
        {
          if (je.ValueKind is JsonValueKind.True or JsonValueKind.False)
          {
            values[patch.TargetField] = je.GetBoolean();
          }
          else if (je.ValueKind == JsonValueKind.String && je.GetString() is { Length: > 0 } jeStr && !string.IsNullOrWhiteSpace(jeStr))
          {
            values[patch.TargetField] = jeStr;
          }
        }
      }
    }
  }

  public static async Task<bool> ApplyServerSelectPatchesAsync<TEntity>(
    PlatformDbContext db,
    PlatformAppService? app,
    IReadOnlyList<FormElementDto> allElements,
    PropertyInfo? boundProperty,
    bool isParentOptional,
    bool isNew,
    TEntity entity,
    IReadOnlyDictionary<string, object?>? originalValues,
    ISet<string>? presentPaths,
    IReadOnlySet<string> protectedPaths,
    IReadOnlySet<string> disabledElementPaths,
    string? cellPatchPath,
    CancellationToken ct)
    where TEntity : class, IEntity
  {
    var systemProtected = FormValueBinder.CreateSystemProtectedSet(boundProperty, isParentOptional, isNew);
    var changedAny = false;
    foreach (var sourceEl in allElements)
    {
      if (sourceEl.SelectPatches is not { Count: > 0 } patches ||
          sourceEl.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } selectModel ||
          string.IsNullOrEmpty(sourceEl.Path) ||
          sourceEl.Path.Contains('.') ||
          sourceEl.IsHidden ||
          sourceEl.IsDisabled ||
          disabledElementPaths.Contains(sourceEl.Path) ||
          sourceEl.IsServerOwned ||
          sourceEl.Model is FormInfoModel)
      {
        continue;
      }

      if (cellPatchPath != null)
      {
        if (!string.Equals(sourceEl.Path, cellPatchPath, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }
      }
      else if (!isNew && presentPaths != null && !presentPaths.Contains(sourceEl.Path))
      {
        continue;
      }

      var sourceProp = PropertyAccessorCache.GetProperty(typeof(TEntity), sourceEl.Path);
      if (sourceProp == null || protectedPaths.Contains(sourceProp.Name))
      {
        continue;
      }

      if (!FormValidator.TryExtractIntegralOrEnumId(PropertyAccessorCache.GetValue(entity, sourceProp.Name), out var selectedId) ||
          selectedId <= 0)
      {
        continue;
      }

      if (!isNew &&
          originalValues != null &&
          originalValues.TryGetValue(sourceProp.Name, out var oldSrcVal) &&
          FormValidator.TryExtractIntegralOrEnumId(oldSrcVal, out var oldSrcId) &&
          oldSrcId == selectedId)
      {
        continue;
      }

      var candidates = new List<(SelectPatchDto Patch, FormElementDto TargetEl, PropertyInfo TargetProp)>();
      foreach (var patch in patches)
      {
        var targetEl = allElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
        var targetProp = PropertyAccessorCache.GetProperty(typeof(TEntity), patch.TargetField);
        if (targetEl == null ||
            targetProp is not { CanWrite: true } ||
            FormValueBinder.IsIgnoredNavigationProperty(targetProp.PropertyType) ||
            systemProtected.Contains(targetProp.Name) ||
            !FormDraftEngine.CanReceiveSelectPatch(typeof(TEntity), targetEl))
        {
          continue;
        }

        var isDynamicallyLocked = !targetEl.IsDisabled && disabledElementPaths.Contains(targetEl.Path);
        if (isDynamicallyLocked)
        {
          continue;
        }

        var needsServerPatch = cellPatchPath != null ||
                               targetEl.IsDisabled ||
                               protectedPaths.Contains(targetProp.Name);
        if (!needsServerPatch)
        {
          continue;
        }

        if (patch.OnlyWhenEmpty && IsNonEmptyEntityValue(PropertyAccessorCache.GetValue(entity, targetProp.Name)))
        {
          continue;
        }

        candidates.Add((patch, targetEl, targetProp));
      }

      if (candidates.Count == 0 ||
          await TryReadReferenceRowAsync(db, app, FormElementBinding.TryGet(sourceEl)?.ReferenceServiceType, refSvc, selectedId, ct) is not { } selectedRowDto)
      {
        continue;
      }

      if (selectModel.ReferenceFilter != null && !MatchesRowFilter(selectedRowDto, selectModel.ReferenceFilter))
      {
        continue;
      }

      foreach (var (patch, targetEl, targetProp) in candidates)
      {
        if (!TryGetRowEntry(selectedRowDto, patch.SourceColumn, out var patchVal) || patchVal == null)
        {
          continue;
        }

        var isReferenceTarget = targetEl.ValueKind == FormValueKind.Reference || targetEl.Model is FormSelectModel;
        if (isReferenceTarget)
        {
          var allowZeroOption = targetEl.Model is FormSelectModel { Options: { Count: > 0 } opts } && opts.Any(static o => o.Id == 0);
          var patchId = ExtractPatchOptionOrReferenceId(patchVal, allowZeroOption);
          if (patchId is null || !allowZeroOption && patchId <= 0 ||
              !await MatchesReferenceTargetFilterAsync(db, app, targetEl, patchId.Value, ct))
          {
            continue;
          }

          FormValueBinder.SetPropertyValue(entity, targetProp.Name, JsonSerializer.SerializeToElement(patchId.Value, PatchJsonOptions));
          presentPaths?.Add(targetEl.Path);
          changedAny = true;
          continue;
        }

        if (patchVal is string sVal && string.IsNullOrWhiteSpace(sVal))
        {
          continue;
        }

        FormValueBinder.SetPropertyValue(entity, targetProp.Name, JsonSerializer.SerializeToElement(patchVal, PatchJsonOptions));
        FormValueBinder.NormalizeNullableStrings([targetEl], entity);
        presentPaths?.Add(targetEl.Path);
        changedAny = true;
      }
    }

    return changedAny;
  }

  private static async Task<RowDto?> TryReadReferenceRowAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    Type? referenceServiceType,
    string referenceService,
    long id,
    CancellationToken ct)
  {
    var svcType = referenceServiceType ?? ModuleCatalog.Instance.FindServiceType(referenceService);
    if (svcType == null ||
        !typeof(IGridService).IsAssignableFrom(svcType) ||
        ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is not IGridService refGrid)
    {
      return null;
    }

    try
    {
      return await refGrid.ReadRowAsync(id, ct);
    }
    catch
    {
      return null;
    }
  }

  private static async Task<bool> MatchesReferenceTargetFilterAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    FormElementDto? targetEl,
    long targetId,
    CancellationToken ct)
  {
    if (targetEl?.Model is not FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc, ReferenceFilter: { } targetFilter })
    {
      return true;
    }

    var targetRow = await TryReadReferenceRowAsync(db, app, FormElementBinding.TryGet(targetEl)?.ReferenceServiceType, targetRefSvc, targetId, ct);
    return targetRow == null || MatchesRowFilter(targetRow, targetFilter);
  }

  private static long? ExtractPatchOptionOrReferenceId(object? val, bool allowZero) =>
    allowZero
      ? val switch
      {
        Reference r => r.Id >= 0 ? r.Id : null,
        IDictionary<string, object?> d when d.TryGetValue("id", out var idObj) && FilterNodeEvaluator.TryConvertToDecimal(idObj, out var dec) && dec >= 0 => (long)dec,
        JsonElement { ValueKind: JsonValueKind.Object } je when FormDraftEngine.TryGetJsonPropertyIgnoreCase(je, "id", out var idProp) && FilterNodeEvaluator.TryConvertToDecimal(idProp, out var jDec) && jDec >= 0 => (long)jDec,
        _ when FilterNodeEvaluator.TryConvertToDecimal(val, out var dVal) && dVal >= 0 => (long)dVal,
        _ => null
      }
      : FormDraftEngine.ExtractReferenceId(val);

  private static bool IsNonEmptyEntityValue(object? val) => val switch
  {
    null => false,
    string str => !string.IsNullOrWhiteSpace(str),
    ICollection col => col.Count > 0,
    _ when FilterNodeEvaluator.TryConvertToDecimal(val, out var dec) => dec != 0m,
    _ => true
  };

  private static bool TryGetRowEntry(RowDto row, string key, out object? value)
  {
    if (row.Cells.TryGetValue(key, out value))
    {
      return true;
    }

    var camel = PropertyAccessorCache.ToCamel(key);
    if (!string.Equals(camel, key, StringComparison.Ordinal) && row.Cells.TryGetValue(camel, out value))
    {
      return true;
    }

    foreach (var kvp in row.Cells)
    {
      if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
      {
        value = kvp.Value;
        return true;
      }
    }

    if (string.Equals(key, "id", StringComparison.OrdinalIgnoreCase) && row.Id.HasValue)
    {
      value = row.Id.Value;
      return true;
    }

    if (string.Equals(key, "ver", StringComparison.OrdinalIgnoreCase) && row.Ver.HasValue)
    {
      value = row.Ver.Value;
      return true;
    }

    value = null;
    return false;
  }

  private static bool MatchesRowFilter(RowDto row, FilterNode filter) =>
    FilterNodeEvaluator.Evaluate(filter, field =>
    {
      if (TryGetRowEntry(row, field, out var cellVal) && cellVal != null)
      {
        return FormDraftEngine.ExtractReferenceId(cellVal) ?? cellVal;
      }

      if (field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
          field.Length > 2 &&
          TryGetRowEntry(row, field[..^2], out cellVal) &&
          cellVal != null)
      {
        return FormDraftEngine.ExtractReferenceId(cellVal) ?? cellVal;
      }

      return null;
    });
}
