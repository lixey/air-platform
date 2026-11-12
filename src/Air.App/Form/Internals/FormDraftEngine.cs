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
    var snapshotElements = GetDraftSnapshotElements(typeof(TEntity), allElements, editableElements);
    var nonEditablePaths = allElements
      .Where(el => el.IsDisabled || el.IsServerOwned || el.IsHidden || el.IsSecret || el.Model is FormInfoModel)
      .Select(el => el.Path)
      .Where(p => !string.IsNullOrEmpty(p))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var sensitiveKeys = GetSensitiveKeys(typeof(TEntity));

    var recordVer = (currentEntity as IHasVersion)?.Ver;
    var baseValues = SerializeDraftSnapshot(modelObj, snapshotElements, ignoreZeroNumbers: !isEdit);
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
        if (!k.Contains('.') && !nonEditablePaths.Contains(k))
        {
          values[k] = v;
          explicitlyProvided.Add(k);
        }
      }
    }

    await FormSelectPatcher.ApplyDraftSelectPatchesAsync(
      db,
      app,
      editableElements,
      snapshotElements,
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
      ? SerializeDraftSnapshot(draftEntity, snapshotElements, ignoreZeroNumbers: false)
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
      var afterComputeSnapshot = SerializeDraftSnapshot(draftEntity, snapshotElements, ignoreZeroNumbers: false);
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

  public static bool CanReceiveSelectPatch(Type entityType, FormElementDto el)
  {
    if (string.IsNullOrEmpty(el.Path) ||
        el.Path.Contains('.') ||
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
  }

  public static List<FormElementDto> GetDraftEditableElements(Type entityType, IReadOnlyList<FormElementDto> allElements) =>
    allElements.Where(el => !el.IsDisabled && CanReceiveSelectPatch(entityType, el)).ToList();

  public static List<FormElementDto> GetDraftSnapshotElements(
    Type entityType,
    IReadOnlyList<FormElementDto> allElements,
    IReadOnlyList<FormElementDto> editableElements)
  {
    var patchTargets = editableElements
      .Where(e => e.SelectPatches is { Count: > 0 })
      .SelectMany(e => e.SelectPatches!.Select(p => p.TargetField))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (patchTargets.Count == 0)
    {
      return [.. editableElements];
    }

    var snapshot = new List<FormElementDto>(editableElements);
    var seen = snapshot.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var el in allElements)
    {
      if (patchTargets.Contains(el.Path) && seen.Add(el.Path) && CanReceiveSelectPatch(entityType, el))
      {
        snapshot.Add(el);
      }
    }

    return snapshot;
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
          curVal is null or "" ||
          !FilterNodeEvaluator.TryConvertToDecimal(curVal, out var curId) ||
          curId < 0m && !select.Options.Any(o => o.Id == (long)curId) ||
          curId == 0m && !select.Options.Any(static o => o.Id == 0))
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

      if (el.Type is UiFieldType.Date or UiFieldType.DateTime)
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

  public static bool TryGetJsonPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
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
