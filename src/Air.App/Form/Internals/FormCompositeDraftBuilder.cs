namespace Platform.App.Form.Internals;

internal static class FormCompositeDraftBuilder
{
  public static async Task<FormCompositeDraftDto> BuildAsync<TEntity>(
    FormService<TEntity> parentForm,
    Type parentFormType,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    FormCompositeDraftRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var isEdit = request.RecordId is > 0;
    var recordId = isEdit ? request.RecordId : null;
    parentForm.SetRequest(new FormRequest { Id = recordId });

    object? existingModel = null;
    if (isEdit)
    {
      existingModel = await parentForm.GetModelAsync(recordId, ct)
        ?? throw new KeyNotFoundException(localizer["FormNotFound"]);
    }

    var (rawElements, allChildren) = await parentForm.GetElementsMetadataAsync(ct);
    if (parentForm.IsFormReadOnly)
    {
      throw Refuse(localizer["FormReadOnly"]);
    }

    var editableElements = rawElements.Where(FormReferenceLookupResolver.IsEditableElement).ToList();
    var initialDraft = await parentForm.PrepareDraftAsync(recordId, ct: ct);
    var recordCaption = isEdit ? await parentForm.GetRecordCaptionAsync(existingModel, recordId, ct) : null;

    var editableChildren = await FormChildDraftParser.DiscoverEditableChildrenAsync(
      parentForm,
      allChildren,
      isEdit,
      recordId,
      db,
      app,
      request.ChildPermissionFilter,
      ct);

    var dictionaries = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
    var refServices = editableElements
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var childDesc in editableChildren)
    {
      foreach (var cel in childDesc.EditableElements)
      {
        if (cel.Model is FormSelectModel cs && !string.IsNullOrEmpty(cs.ReferenceService))
        {
          refServices.Add(cs.ReferenceService);
        }
      }
    }

    await FormReferenceLookupResolver.PreloadSmallDictionariesAsync(
      refServices,
      request.MaxPreloadDictionarySize,
      dictionaries,
      parentFormType,
      db,
      app,
      ct);

    await PreloadInitialReferenceValuesAsync(editableElements, initialDraft.Values, dictionaries, parentFormType, db, app, ct);

    var argsObj = request.Args ?? default;
    var valuesObj = ResolveValuesElement(request, argsObj);

    var formValues = FormDraftEngine.CloneDraftValues(initialDraft.Values);
    var explicitFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var unresolvedRefs = new List<FormUnresolvedReferenceDto>();

    await ParseParentFieldsAsync(
      editableElements,
      valuesObj,
      argsObj,
      isEdit,
      request.MultiSelectMode,
      formValues,
      explicitFields,
      unresolvedRefs,
      dictionaries,
      parentFormType,
      request.CreateFormResolver,
      db,
      app,
      localizer,
      ct);

    var pendingHeaderPaths = unresolvedRefs
      .Where(r => r.ChildKey == null)
      .Select(r => r.FieldPath)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var headerOverrides = BuildHeaderOverrides(explicitFields, pendingHeaderPaths, formValues);
    var preparedDraft = await parentForm.PrepareDraftAsync(recordId, headerOverrides, pendingHeaderPaths, ct: ct);
    MergePreparedDraftValues(preparedDraft.Values, pendingHeaderPaths, formValues);
    await PreloadInitialReferenceValuesAsync(editableElements, formValues, dictionaries, parentFormType, db, app, ct);

    var (childGroups, childMessages, appliedAnySumTo) = await FormChildDraftParser.BuildChildDraftsAsync(
      parentForm,
      allChildren,
      editableChildren,
      argsObj,
      valuesObj,
      isEdit,
      recordId,
      formValues,
      explicitFields,
      unresolvedRefs,
      dictionaries,
      request.CreateFormResolver,
      db,
      app,
      localizer,
      ct);

    if (appliedAnySumTo)
    {
      headerOverrides = BuildHeaderOverrides(explicitFields, pendingHeaderPaths, formValues);
      preparedDraft = await parentForm.PrepareDraftAsync(recordId, headerOverrides, pendingHeaderPaths, ct: ct);
      MergePreparedDraftValues(preparedDraft.Values, pendingHeaderPaths, formValues);
    }

    var combinedMessages = new List<MessageDto>(preparedDraft.Messages.Count + childMessages.Count);
    combinedMessages.AddRange(preparedDraft.Messages);
    combinedMessages.AddRange(childMessages);

    return new FormCompositeDraftDto
    {
      RecordId = recordId,
      Ver = preparedDraft.Ver ?? initialDraft.Ver,
      RecordCaption = recordCaption,
      OriginalValues = initialDraft.OriginalValues,
      Values = formValues,
      ExplicitFields = explicitFields,
      Elements = rawElements,
      Children = childGroups,
      UnresolvedReferences = unresolvedRefs,
      Dictionaries = dictionaries,
      Messages = combinedMessages
    };
  }

  private static async Task PreloadInitialReferenceValuesAsync(
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> initialValues,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type parentFormType,
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      if (el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } sel)
      {
        continue;
      }

      if (sel.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        foreach (var existingId in FormReferenceLookupResolver.ExtractLongList(initialValues.GetValueOrDefault(el.Path)))
        {
          await FormReferenceLookupResolver.EnsureReferenceIdLoadedAsync(refSvc, existingId, dictionaries, parentFormType, db, app, ct);
        }
      }
      else if (initialValues.TryGetValue(el.Path, out var curVal) &&
               FormReferenceLookupResolver.TryConvertToLong(curVal, out var curId) &&
               curId > 0)
      {
        await FormReferenceLookupResolver.EnsureReferenceIdLoadedAsync(refSvc, curId, dictionaries, parentFormType, db, app, ct);
      }
    }
  }

  private static Dictionary<string, object?> BuildHeaderOverrides(
    HashSet<string> explicitFields,
    HashSet<string> pendingHeaderPaths,
    Dictionary<string, object?> formValues) =>
    explicitFields
      .Where(k => !pendingHeaderPaths.Contains(k))
      .ToDictionary(k => k, k => formValues.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);

  private static void MergePreparedDraftValues(
    Dictionary<string, object?> draftValues,
    HashSet<string> pendingPaths,
    Dictionary<string, object?> targetValues)
  {
    foreach (var (k, v) in draftValues)
    {
      if (!pendingPaths.Contains(k))
      {
        targetValues[k] = v;
      }
    }
  }

  private static JsonElement ResolveValuesElement(FormCompositeDraftRequest request, JsonElement argsObj)
  {
    if (request.Values is { ValueKind: JsonValueKind.Object } vObj)
    {
      return vObj;
    }

    if (argsObj.ValueKind == JsonValueKind.Object &&
        FormDraftEngine.TryGetJsonPropertyIgnoreCase(argsObj, "values", out var nestedVal) &&
        nestedVal.ValueKind == JsonValueKind.Object)
    {
      return nestedVal;
    }

    return argsObj;
  }

  private static async Task ParseParentFieldsAsync(
    IReadOnlyList<FormElementDto> editableElements,
    JsonElement valuesObj,
    JsonElement argsObj,
    bool isEdit,
    string multiSelectMode,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitFields,
    List<FormUnresolvedReferenceDto> unresolvedRefs,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type referrerType,
    Func<FormElementDto, string, string?>? createFormResolver,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      var select = el.Model as FormSelectModel;
      if (select != null && (select.IsMultiple || el.ValueKind == FormValueKind.List))
      {
        await ParseMultiSelectFieldAsync(
          el,
          select,
          valuesObj,
          argsObj,
          isEdit,
          multiSelectMode,
          formValues,
          explicitFields,
          unresolvedRefs,
          dictionaries,
          referrerType,
          createFormResolver,
          db,
          app,
          localizer,
          ct);
        continue;
      }

      if (select != null && !string.IsNullOrEmpty(select.ReferenceService))
      {
        await ParseSingleReferenceFieldAsync(
          el,
          select,
          valuesObj,
          argsObj,
          formValues,
          explicitFields,
          unresolvedRefs,
          dictionaries,
          referrerType,
          createFormResolver,
          db,
          app,
          localizer,
          ct);
        continue;
      }

      if (TryReadInput(valuesObj, argsObj, el.Path, out var rawScalar) && rawScalar.ValueKind != JsonValueKind.Undefined)
      {
        formValues[el.Path] = FormReferenceLookupResolver.ConvertJsonScalar(rawScalar, el.Type, select);
        explicitFields.Add(el.Path);
      }
    }
  }

  private static async Task ParseMultiSelectFieldAsync(
    FormElementDto el,
    FormSelectModel select,
    JsonElement valuesObj,
    JsonElement argsObj,
    bool isEdit,
    string multiSelectMode,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitFields,
    List<FormUnresolvedReferenceDto> unresolvedRefs,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type referrerType,
    Func<FormElementDto, string, string?>? createFormResolver,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct)
  {
    var hasMulti = TryReadInput(valuesObj, argsObj, el.Path, out var rawMulti);
    if (!hasMulti && el.Path.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 3)
    {
      var baseMulti = el.Path[..^3];
      hasMulti = TryReadInput(valuesObj, argsObj, $"{baseMulti}s", out rawMulti) ||
                 TryReadInput(valuesObj, argsObj, baseMulti, out rawMulti);
    }

    if (!hasMulti)
    {
      return;
    }

    if (rawMulti.ValueKind == JsonValueKind.Array)
    {
      var ids = isEdit && string.Equals(multiSelectMode, "append", StringComparison.OrdinalIgnoreCase)
        ? FormReferenceLookupResolver.ExtractLongList(formValues.GetValueOrDefault(el.Path))
        : [];
      var createForm = !string.IsNullOrEmpty(select.ReferenceService)
        ? createFormResolver?.Invoke(el, select.ReferenceService)
        : null;

      foreach (var item in rawMulti.EnumerateArray())
      {
        if (FormReferenceLookupResolver.TryExtractSymbolicRefKey(item, out var symKey))
        {
          unresolvedRefs.Add(new FormUnresolvedReferenceDto
          {
            FieldPath = el.Path,
            IsMultiple = true,
            SymbolicRefKey = symKey,
            ReferenceService = select.ReferenceService
          });
          continue;
        }

        var (itemId, unresolvedName, _) = await FormReferenceLookupResolver.ResolveReferenceValueAsync(
          item,
          select.ReferenceService ?? string.Empty,
          select.ReferenceFilter,
          allowFuzzy: createForm == null,
          allowWrongKindFallback: false,
          dictionaries,
          referrerType,
          db,
          app,
          ct,
          select.ReferenceLabelPaths);
        if (itemId is > 0 and var id)
        {
          if (!ids.Contains(id))
          {
            ids.Add(id);
          }
        }
        else if (!string.IsNullOrWhiteSpace(unresolvedName))
        {
          unresolvedRefs.Add(new FormUnresolvedReferenceDto
          {
            FieldPath = el.Path,
            IsMultiple = true,
            RawName = unresolvedName,
            ReferenceService = select.ReferenceService,
            CreateFormService = createForm,
            ExtraValues = Platform.App.Internals.FilterNodeEvaluator.ExtractEqualityValues(select.ReferenceFilter)
          });
        }
        else if (TryExtractExplicitPositiveReferenceId(item, out var badMultiId))
        {
          throw Refuse(localizer["ReferenceNotFound", $"#{badMultiId}", select.ReferenceService ?? string.Empty]);
        }
      }

      formValues[el.Path] = ids;
      explicitFields.Add(el.Path);
    }
    else if (rawMulti.ValueKind == JsonValueKind.Null)
    {
      formValues[el.Path] = new List<long>();
      explicitFields.Add(el.Path);
    }
  }

  private static async Task ParseSingleReferenceFieldAsync(
    FormElementDto el,
    FormSelectModel select,
    JsonElement valuesObj,
    JsonElement argsObj,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitFields,
    List<FormUnresolvedReferenceDto> unresolvedRefs,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type referrerType,
    Func<FormElementDto, string, string?>? createFormResolver,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct)
  {
    var hasRef = TryReadInput(valuesObj, argsObj, el.Path, out var rawRef);
    if (!hasRef && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
    {
      var baseRef = el.Path[..^2];
      hasRef = TryReadInput(valuesObj, argsObj, $"{baseRef}Name", out rawRef) ||
               TryReadInput(valuesObj, argsObj, baseRef, out rawRef);
    }

    if (!hasRef)
    {
      return;
    }

    if (FormReferenceLookupResolver.TryExtractSymbolicRefKey(rawRef, out var symKey))
    {
      formValues[el.Path] = null;
      explicitFields.Add(el.Path);
      unresolvedRefs.Add(new FormUnresolvedReferenceDto
      {
        FieldPath = el.Path,
        IsMultiple = false,
        SymbolicRefKey = symKey,
        ReferenceService = select.ReferenceService
      });
      return;
    }

    var createForm = createFormResolver?.Invoke(el, select.ReferenceService!);
    var (resolvedId, unresolvedName, objectExtra) = await FormReferenceLookupResolver.ResolveReferenceValueAsync(
      rawRef,
      select.ReferenceService!,
      select.ReferenceFilter,
      allowFuzzy: createForm == null,
      allowWrongKindFallback: true,
      dictionaries,
      referrerType,
      db,
      app,
      ct,
      select.ReferenceLabelPaths);

    if (resolvedId.HasValue)
    {
      formValues[el.Path] = resolvedId.Value;
      explicitFields.Add(el.Path);
    }
    else if (!string.IsNullOrWhiteSpace(unresolvedName))
    {
      formValues[el.Path] = null;
      explicitFields.Add(el.Path);
      var extra = Platform.App.Internals.FilterNodeEvaluator.ExtractEqualityValues(select.ReferenceFilter);
      if (objectExtra != null)
      {
        foreach (var (k, v) in objectExtra)
        {
          extra[k] = v;
        }
      }

      unresolvedRefs.Add(new FormUnresolvedReferenceDto
      {
        FieldPath = el.Path,
        IsMultiple = false,
        RawName = unresolvedName,
        ReferenceService = select.ReferenceService,
        CreateFormService = createForm,
        ExtraValues = extra
      });
    }
    else if (rawRef.ValueKind == JsonValueKind.Null ||
             rawRef.ValueKind == JsonValueKind.Number && rawRef.TryGetInt64(out var zeroId) && zeroId <= 0)
    {
      formValues[el.Path] = null;
      explicitFields.Add(el.Path);
    }
    else if (TryExtractExplicitPositiveReferenceId(rawRef, out var badId))
    {
      throw Refuse(localizer["ReferenceNotFound", $"#{badId}", select.ReferenceService!]);
    }
  }

  internal static bool TryExtractExplicitPositiveReferenceId(JsonElement el, out long id)
  {
    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out id) && id > 0)
    {
      return true;
    }

    if (el.ValueKind == JsonValueKind.Object && FormReferenceLookupResolver.TryGetLong(el, "id") is > 0 and var objId)
    {
      id = objId;
      return true;
    }

    id = 0;
    return false;
  }

  internal static bool TryReadInput(JsonElement valuesObj, JsonElement argsObj, string name, out JsonElement value)
  {
    if (FormDraftEngine.TryGetJsonPropertyIgnoreCase(valuesObj, name, out value))
    {
      return true;
    }

    if (!valuesObj.Equals(argsObj) && FormDraftEngine.TryGetJsonPropertyIgnoreCase(argsObj, name, out value))
    {
      return true;
    }

    value = default;
    return false;
  }

  internal static FormRefusedException Refuse(string message) =>
    new(MutationOutcome.Refused, message, [new MessageDto { Detail = message, Type = MessageType.Error }]);
}
