using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormDraftEngine
{
  private static readonly JsonSerializerOptions DraftJsonOptions = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
  };

  public static async Task<FormDraftResultDto> BuildDraftResultAsync<TEntity>(
    PlatformDbContext db,
    PlatformAppService? app,
    IReadOnlyList<FormElementDto> allElements,
    object? modelObj,
    TEntity? currentEntity,
    bool isEdit,
    IReadOnlyDictionary<string, object?>? overrides,
    ISet<string>? pendingReferencePaths,
    string? triggerField,
    bool hasComputeHooks,
    Func<TEntity, IReadOnlyDictionary<string, object?>, IReadOnlySet<string>, Task<(IReadOnlyList<MessageDto> SaveErrors, IReadOnlyList<MessageDto> RuleFailures)>> runValidationAsync,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var editableElements = GetDraftEditableElements(typeof(TEntity), allElements);
    var sensitiveKeys = GetSensitiveKeys(typeof(TEntity));

    var recordVer = (currentEntity as IHasVersion)?.Ver;
    var baseValues = SerializeDraftSnapshot(modelObj, editableElements, ignoreZeroNumbers: !isEdit);
    var originalValues = isEdit
      ? CloneDraftValues(baseValues)
      : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var values = CloneDraftValues(baseValues);

    if (overrides is not { Count: > 0 } && pendingReferencePaths is not { Count: > 0 })
    {
      StripSensitiveKeys(sensitiveKeys, originalValues);
      StripSensitiveKeys(sensitiveKeys, values);
      return new FormDraftResultDto
      {
        OriginalValues = originalValues,
        Values = values,
        Ver = recordVer
      };
    }

    var explicitlyProvided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (overrides != null)
    {
      foreach (var (k, v) in overrides)
      {
        if (!k.Contains('.'))
        {
          values[k] = v;
          explicitlyProvided.Add(k);
        }
      }
    }

    await ApplyDraftSelectPatchesAsync(
      db,
      app,
      editableElements,
      originalValues,
      values,
      explicitlyProvided,
      pendingReferencePaths,
      isEdit,
      triggerField,
      ct);

    ApplyDefaultRequiredSelectOptions(editableElements, explicitlyProvided, pendingReferencePaths, values);

    var draftEntity = new TEntity();
    if (currentEntity != null)
    {
      FormValueBinder.CopyMatchingProperties(currentEntity, draftEntity);
      draftEntity.Id = currentEntity.Id;
    }

    ApplyMergedValuesToDraftEntity(values, draftEntity);

    var preComputeSnapshot = hasComputeHooks
      ? SerializeDraftSnapshot(draftEntity, editableElements, ignoreZeroNumbers: false)
      : null;

    List<MessageDto> messages = [];
    try
    {
      var (saveErrors, ruleFailures) = await runValidationAsync(draftEntity, originalValues, explicitlyProvided);
      if (saveErrors.Count > 0)
      {
        messages.AddRange(saveErrors);
      }

      if (ruleFailures.Count > 0)
      {
        messages.AddRange(ruleFailures);
      }
    }
    catch
    {
    }

    if (preComputeSnapshot != null)
    {
      var afterComputeSnapshot = SerializeDraftSnapshot(draftEntity, editableElements, ignoreZeroNumbers: false);
      MergeComputedChanges(preComputeSnapshot, afterComputeSnapshot, explicitlyProvided, values);
    }

    StripSensitiveKeys(sensitiveKeys, originalValues);
    StripSensitiveKeys(sensitiveKeys, values);

    return new FormDraftResultDto
    {
      OriginalValues = originalValues,
      Values = values,
      Ver = recordVer,
      Messages = messages
    };
  }

  public static long? ExtractReferenceId(object? val) => val switch
  {
    Reference r => r.Id > 0 ? r.Id : null,
    IDictionary<string, object?> d when d.TryGetValue("id", out var idObj) && FilterNodeEvaluator.TryConvertToDecimal(idObj, out var dec) && dec > 0 => (long)dec,
    JsonElement { ValueKind: JsonValueKind.Object } je when TryGetJsonPropertyIgnoreCase(je, "id", out var idProp) && FilterNodeEvaluator.TryConvertToDecimal(idProp, out var jDec) && jDec > 0 => (long)jDec,
    _ when FilterNodeEvaluator.TryConvertToDecimal(val, out var dVal) && dVal > 0 => (long)dVal,
    _ => null
  };

  public static List<FormElementDto> GetDraftEditableElements(Type entityType, IReadOnlyList<FormElementDto> allElements)
  {
    return allElements
      .Where(el =>
      {
        if (string.IsNullOrEmpty(el.Path) ||
            el.Path.Contains('.') ||
            el.IsDisabled ||
            el.IsServerOwned ||
            el.IsHidden ||
            el.IsSecret ||
            string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase) ||
            el.Model is FormInfoModel)
        {
          return false;
        }

        var prop = PropertyAccessorCache.GetProperty(entityType, el.Path);
        return prop == null || !Attribute.IsDefined(prop, typeof(SensitiveAttribute));
      })
      .ToList();
  }

  public static HashSet<string> GetSensitiveKeys(Type entityType)
  {
    var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var prop in PropertyAccessorCache.GetProperties(entityType))
    {
      if (Attribute.IsDefined(prop, typeof(SensitiveAttribute)))
      {
        sensitive.Add(prop.Name);
        sensitive.Add(Elements.ToCamelPath(prop.Name));
        sensitive.Add(JsonNamingPolicy.CamelCase.ConvertName(prop.Name));
      }
    }

    return sensitive;
  }

  public static void StripSensitiveKeys(HashSet<string> sensitiveKeys, Dictionary<string, object?> dict)
  {
    if (sensitiveKeys.Count == 0 || dict.Count == 0)
    {
      return;
    }

    foreach (var key in dict.Keys.ToList())
    {
      if (sensitiveKeys.Contains(key))
      {
        dict.Remove(key);
      }
    }
  }

  public static Dictionary<string, object?> SerializeDraftSnapshot(
    object? modelObj,
    IReadOnlyList<FormElementDto> editableElements,
    bool ignoreZeroNumbers)
  {
    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (modelObj == null)
    {
      return values;
    }

    var jsonEl = JsonSerializer.SerializeToElement(modelObj, DraftJsonOptions);
    if (jsonEl.ValueKind == JsonValueKind.Object)
    {
      PopulateDraftValuesFromJson(jsonEl, editableElements, values, ignoreZeroNumbers);
    }

    return values;
  }

  public static Dictionary<string, object?> CloneDraftValues(Dictionary<string, object?> source)
  {
    var clone = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in source)
    {
      clone[k] = v is IEnumerable<long> list ? list.ToList() : v;
    }

    return clone;
  }

  public static void ApplyMergedValuesToDraftEntity<TEntity>(Dictionary<string, object?> values, TEntity draftEntity)
    where TEntity : class, IEntity
  {
    var mergedJson = JsonSerializer.SerializeToElement(values, DraftJsonOptions);
    if (mergedJson.ValueKind == JsonValueKind.Object)
    {
      foreach (var prop in mergedJson.EnumerateObject())
      {
        FormValueBinder.SetPropertyValue(draftEntity, prop.Name, prop.Value);
      }
    }
  }

  public static void MergeComputedChanges(
    Dictionary<string, object?> preCompute,
    Dictionary<string, object?> afterCompute,
    HashSet<string> explicitlyProvided,
    Dictionary<string, object?> targetValues)
  {
    foreach (var (k, afterVal) in afterCompute)
    {
      var existedBefore = preCompute.TryGetValue(k, out var beforeVal);
      var changedByCompute = !existedBefore || !AreDraftValuesEqual(beforeVal, afterVal);
      if (changedByCompute)
      {
        targetValues[k] = afterVal;
      }
      else if (afterVal != null && !explicitlyProvided.Contains(k) && targetValues.ContainsKey(k))
      {
        targetValues[k] = afterVal;
      }
    }
  }

  public static async Task ApplyDraftSelectPatchesAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    IReadOnlyList<FormElementDto> editableElements,
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

      var svcType = ModuleCatalog.Instance.FindServiceType(refSvc);
      if (svcType == null ||
          !typeof(IGridService).IsAssignableFrom(svcType) ||
          ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is not IGridService refGrid)
      {
        continue;
      }

      object? rawRow;
      try
      {
        rawRow = await refGrid.ReadRowAsync(selectedId, ct);
      }
      catch
      {
        continue;
      }

      if (rawRow is not IDictionary<string, object?> selectedRow)
      {
        continue;
      }

      foreach (var patch in patches)
      {
        if (!hasTriggerField && explicitlyProvided.Contains(patch.TargetField) ||
            pendingReferencePaths?.Contains(patch.TargetField) == true)
        {
          continue;
        }

        if ((!isEdit && !hasTriggerField || patch.OnlyWhenEmpty) &&
            values.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            !(FilterNodeEvaluator.TryConvertToDecimal(existingTarget, out var existingDec) && existingDec == 0m) &&
            existingTarget.ToString() is { Length: > 0 } existingStr &&
            existingStr != "0")
        {
          continue;
        }

        if (!selectedRow.TryGetValue(patch.SourceColumn, out var patchVal) || patchVal == null)
        {
          continue;
        }

        var targetEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
        var isReferenceTarget = targetEl?.ValueKind == FormValueKind.Reference || targetEl?.Model is FormSelectModel;
        if (isReferenceTarget)
        {
          var patchId = ExtractReferenceId(patchVal);
          if (patchId is not > 0)
          {
            continue;
          }

          if (targetEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc, ReferenceFilter: { } targetFilter })
          {
            var targetSvcType = ModuleCatalog.Instance.FindServiceType(targetRefSvc);
            if (targetSvcType != null &&
                typeof(IGridService).IsAssignableFrom(targetSvcType) &&
                ModuleCatalog.Instance.CreateServiceInstance(targetSvcType, db, app) is IGridService targetGrid)
            {
              try
              {
                if (await targetGrid.ReadRowAsync(patchId.Value, ct) is IDictionary<string, object?> targetRow &&
                    !MatchesRowFilter(targetRow, targetFilter))
                {
                  continue;
                }
              }
              catch
              {
              }
            }
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

  public static void ApplyDefaultRequiredSelectOptions(
    IReadOnlyList<FormElementDto> editableElements,
    HashSet<string> explicitlyProvided,
    ISet<string>? pendingReferencePaths,
    Dictionary<string, object?> values)
  {
    foreach (var el in editableElements)
    {
      if (!el.IsRequired ||
          el.Model is not FormSelectModel { Options.Count: > 0 } select ||
          select.IsMultiple ||
          el.ValueKind == FormValueKind.List)
      {
        continue;
      }

      if (explicitlyProvided.Contains(el.Path) || pendingReferencePaths?.Contains(el.Path) == true)
      {
        continue;
      }

      if (!values.TryGetValue(el.Path, out var curVal) ||
          !FilterNodeEvaluator.TryConvertToDecimal(curVal, out var curId) ||
          curId <= 0)
      {
        values[el.Path] = select.Options[0].Id;
      }
    }
  }

  private static bool AreDraftValuesEqual(object? left, object? right)
  {
    if (ReferenceEquals(left, right))
    {
      return true;
    }

    if (left is null || right is null)
    {
      return false;
    }

    if (FilterNodeEvaluator.TryConvertToDecimal(left, out var lDec) &&
        FilterNodeEvaluator.TryConvertToDecimal(right, out var rDec))
    {
      return lDec == rDec;
    }

    if (left is IEnumerable<long> lList && right is IEnumerable<long> rList)
    {
      return lList.SequenceEqual(rList);
    }

    return Equals(left, right);
  }

  private static bool MatchesRowFilter(IDictionary<string, object?> row, FilterNode filter) =>
    FilterNodeEvaluator.Evaluate(filter, field =>
    {
      if (row.TryGetValue(field, out var cellVal) && cellVal != null)
      {
        return ExtractReferenceId(cellVal) ?? cellVal;
      }

      if (field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
          field.Length > 2 &&
          row.TryGetValue(field[..^2], out cellVal) &&
          cellVal != null)
      {
        return ExtractReferenceId(cellVal) ?? cellVal;
      }

      return null;
    });

  private static void PopulateDraftValuesFromJson(
    JsonElement jsonEl,
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> target,
    bool ignoreZeroNumbers)
  {
    foreach (var el in editableElements)
    {
      if (!TryGetJsonPropertyIgnoreCase(jsonEl, el.Path, out var prop) ||
          prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
      {
        continue;
      }

      if (el.Model is FormSelectModel select)
      {
        if (select.IsMultiple || el.ValueKind == FormValueKind.List)
        {
          if (prop.ValueKind == JsonValueKind.Array)
          {
            var ids = new List<long>();
            foreach (var item in prop.EnumerateArray())
            {
              if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id) && id > 0)
              {
                ids.Add(id);
              }
            }

            if (!ignoreZeroNumbers || ids.Count > 0)
            {
              target[el.Path] = ids;
            }
          }
        }
        else if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var refId) && (refId > 0 || !ignoreZeroNumbers && select.Options?.Any(o => o.Id == refId) == true))
        {
          target[el.Path] = refId;
        }
        else if (prop.ValueKind == JsonValueKind.Object &&
                 TryGetJsonPropertyIgnoreCase(prop, "id", out var idProp) &&
                 idProp.ValueKind == JsonValueKind.Number &&
                 idProp.TryGetInt64(out var objRefId) &&
                 objRefId > 0)
        {
          target[el.Path] = objRefId;
        }

        continue;
      }

      if (el.Type is UiFieldType.Date or UiFieldType.Datetime)
      {
        if (prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { Length: > 0 } dateStr &&
            !dateStr.StartsWith("0001-01-01", StringComparison.Ordinal))
        {
          target[el.Path] = dateStr;
        }

        continue;
      }

      if (el.Type == UiFieldType.Checkbox && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
      {
        target[el.Path] = prop.GetBoolean();
        continue;
      }

      if (el.Type == UiFieldType.Number && prop.ValueKind == JsonValueKind.Number)
      {
        if (!ignoreZeroNumbers && el.Model is not FormMoneyModel && prop.TryGetInt64(out var lngVal))
        {
          target[el.Path] = lngVal;
        }
        else if (prop.TryGetDecimal(out var decVal) && (!ignoreZeroNumbers || decVal != 0m))
        {
          target[el.Path] = decVal;
        }

        continue;
      }

      if (prop.ValueKind == JsonValueKind.String && prop.GetString() is { } str && (!ignoreZeroNumbers || str.Length > 0))
      {
        target[el.Path] = str;
      }
    }
  }

  private static bool TryGetJsonPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      if (element.TryGetProperty(name, out value))
      {
        return true;
      }

      foreach (var prop in element.EnumerateObject())
      {
        if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
        {
          value = prop.Value;
          return true;
        }
      }
    }

    value = default;
    return false;
  }
}
