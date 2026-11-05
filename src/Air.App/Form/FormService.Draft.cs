using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
{
  private static readonly JsonSerializerOptions DraftJsonOptions = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
  };

  public virtual async Task<FormDraftResultDto> PrepareDraftAsync(
    long? recordId = null,
    IReadOnlyDictionary<string, object?>? overrides = null,
    ISet<string>? pendingReferencePaths = null,
    long? parentId = null,
    string? parentService = null,
    CancellationToken ct = default)
  {
    var isEdit = recordId is > 0;
    var req = new FormRequest
    {
      Id = isEdit ? recordId : null,
      ParentId = parentId,
      ParentService = parentService
    };
    SetRequest(req);

    var (elementDtos, _) = await GetElementsMetadataAsync(ct);
    var editableElements = elementDtos
      .Where(static el =>
        !string.IsNullOrEmpty(el.Path) &&
        !el.IsDisabled &&
        !el.IsServerOwned &&
        !el.IsHidden &&
        !el.IsSecret &&
        !string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase) &&
        el.Model is not FormInfoModel)
      .ToList();

    object? modelObj = null;
    try
    {
      modelObj = await GetModelAsync(req, ct);
    }
    catch when (!isEdit)
    {
      // Ignore default model resolution errors when parent context is not yet created
    }

    var recordVer = (CurrentEntity as IHasVersion)?.Ver;
    var baseValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (modelObj != null)
    {
      var jsonEl = JsonSerializer.SerializeToElement(modelObj, DraftJsonOptions);
      if (jsonEl.ValueKind == JsonValueKind.Object)
      {
        PopulateDraftValuesFromJson(jsonEl, editableElements, baseValues, ignoreZeroNumbers: !isEdit);
      }
    }

    var originalValues = isEdit
      ? CloneDraftValues(baseValues)
      : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var values = CloneDraftValues(baseValues);

    if (overrides is not { Count: > 0 } && pendingReferencePaths is not { Count: > 0 })
    {
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
        values[k] = v;
        explicitlyProvided.Add(k);
      }
    }

    await ApplyDraftSelectPatchesAsync(
      editableElements,
      originalValues,
      values,
      explicitlyProvided,
      pendingReferencePaths,
      isEdit,
      ct);

    foreach (var el in editableElements)
    {
      if (!el.IsRequired ||
          el.Model is not FormSelectModel { Options.Count: > 0 } select ||
          select.IsMultiple ||
          el.ValueKind == FormValueKind.List)
      {
        continue;
      }

      if (pendingReferencePaths?.Contains(el.Path) == true)
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

    var elements = await EnsureElementsAsync(ct);
    var hooks = GetHooks();

    var draftEntity = new TEntity();
    if (CurrentEntity != null)
    {
      CopyMatchingProperties(CurrentEntity, draftEntity);
      draftEntity.Id = CurrentEntity.Id;
    }

    var mergedJson = JsonSerializer.SerializeToElement(values, DraftJsonOptions);
    if (mergedJson.ValueKind == JsonValueKind.Object)
    {
      foreach (var prop in mergedJson.EnumerateObject())
      {
        SetPropertyValue(draftEntity, prop.Name, prop.Value);
      }
    }

    List<MessageDto> messages = [];
    try
    {
      var (_, ruleCtx) = await RunValidationPipelineAsync(
        elements,
        hooks,
        draftEntity,
        originalValues,
        explicitlyProvided,
        ct);
      messages = ruleCtx.Failures;
    }
    catch
    {
      // Non-fatal during draft preparation when parent or external context is not yet persisted
    }

    if (hooks.BeforeValidateActions.Count > 0)
    {
      var computedJson = JsonSerializer.SerializeToElement(draftEntity, DraftJsonOptions);
      if (computedJson.ValueKind == JsonValueKind.Object)
      {
        var afterCompute = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        PopulateDraftValuesFromJson(computedJson, editableElements, afterCompute, ignoreZeroNumbers: false);
        foreach (var (k, v) in afterCompute)
        {
          if (v != null &&
              (!explicitlyProvided.Contains(k) ||
               FilterNodeEvaluator.TryConvertToDecimal(v, out var numV) && numV != 0m))
          {
            values[k] = v;
          }
        }
      }
    }

    return new FormDraftResultDto
    {
      OriginalValues = originalValues,
      Values = values,
      Ver = recordVer,
      Messages = messages
    };
  }

  private async Task ApplyDraftSelectPatchesAsync(
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> originalValues,
    Dictionary<string, object?> values,
    HashSet<string> explicitlyProvided,
    ISet<string>? pendingReferencePaths,
    bool isEdit,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      if (isEdit && !explicitlyProvided.Contains(el.Path))
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
          originalValues.TryGetValue(el.Path, out var oldSrcVal) &&
          FilterNodeEvaluator.TryConvertToDecimal(oldSrcVal, out var oldSrcDec) &&
          (long)oldSrcDec == selectedId)
      {
        continue;
      }

      var svcType = ModuleCatalog.Instance.FindServiceType(refSvc);
      if (svcType == null ||
          !typeof(IGridService).IsAssignableFrom(svcType) ||
          ModuleCatalog.Instance.CreateServiceInstance(svcType, db, PlatformApp) is not IGridService refGrid)
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
        if (explicitlyProvided.Contains(patch.TargetField) ||
            pendingReferencePaths?.Contains(patch.TargetField) == true)
        {
          continue;
        }

        if ((!isEdit || patch.OnlyWhenEmpty) &&
            values.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            existingTarget.ToString() is { Length: > 0 } existingStr &&
            existingStr != "0")
        {
          continue;
        }

        if (!selectedRow.TryGetValue(patch.SourceColumn, out var patchVal) || patchVal == null)
        {
          continue;
        }

        var patchId = ExtractReferenceId(patchVal);
        if (patchId is not > 0)
        {
          continue;
        }

        var targetEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
        if (targetEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc, ReferenceFilter: { } targetFilter })
        {
          var targetSvcType = ModuleCatalog.Instance.FindServiceType(targetRefSvc);
          if (targetSvcType != null &&
              typeof(IGridService).IsAssignableFrom(targetSvcType) &&
              ModuleCatalog.Instance.CreateServiceInstance(targetSvcType, db, PlatformApp) is IGridService targetGrid)
          {
            try
            {
              if (await targetGrid.ReadRowAsync(patchId.Value, ct) is IDictionary<string, object?> targetRow &&
                  !MatchesRowFilter(targetRow, targetFilter))
              {
                continue;
              }
            }
            catch { }
          }
        }

        values[patch.TargetField] = patchId.Value;
      }
    }
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

  private static long? ExtractReferenceId(object? val) => val switch
  {
    Reference r => r.Id > 0 ? r.Id : null,
    IDictionary<string, object?> d when d.TryGetValue("id", out var idObj) && FilterNodeEvaluator.TryConvertToDecimal(idObj, out var dec) && dec > 0 => (long)dec,
    _ when FilterNodeEvaluator.TryConvertToDecimal(val, out var dVal) && dVal > 0 => (long)dVal,
    _ => null
  };

  private static Dictionary<string, object?> CloneDraftValues(Dictionary<string, object?> source)
  {
    var clone = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in source)
    {
      clone[k] = v is IEnumerable<long> list ? list.ToList() : v;
    }

    return clone;
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
        else if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var refId) && refId > 0)
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

      if (prop.ValueKind == JsonValueKind.String && prop.GetString() is { Length: > 0 } str)
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
