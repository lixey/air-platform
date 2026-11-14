using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form.Internals;

internal static class ElementResolver
{
  public static void ConfigureStructuralElements<TEntity>(
    ElementResolveScope scope,
    Elements elements)
    where TEntity : class
  {
    foreach (var child in elements.Children)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(scope.OwnerServiceType, child.ServiceType, $"AddChild '{child.DeclaredKey}'");
    }

    if (elements.StatusFlow is IRecordStatuses statusFlow)
    {
      var statusMemberName = StatusMoveNavBuilder.ExtractStatusMemberName(statusFlow.StatusMemberExpression);
      var statusOptions = statusFlow.Statuses.Select(s => new OptionDto
      {
        Id = s.Id,
        Name = s.Name,
        Color = s.Color,
        Title = s.Title
      }).ToList();

      foreach (var el in elements.List)
      {
        var isStatusField = statusMemberName != null
          ? string.Equals(el.Path, statusMemberName, StringComparison.OrdinalIgnoreCase)
          : el.Path.EndsWith("statusId", StringComparison.OrdinalIgnoreCase)
            || el.Path.Equals("status", StringComparison.OrdinalIgnoreCase);

        if (isStatusField)
        {
          el.IsDisabled = true;
          el.IsServerOwned = true;
          el.Type = UiFieldType.Text;
          el.Model = new FormSelectModel { Options = statusOptions };
        }
      }
    }

    var efEntityType = scope.Db?.Model.FindEntityType(typeof(TEntity));
    FormConstraintExtractor.ApplyElementConstraints(typeof(TEntity), efEntityType, elements.List);

    foreach (var el in elements.List)
    {
      var binding = FormElementBinding.TryGet(el);
      if (el.Model is FormSelectModel select && !string.IsNullOrEmpty(select.ReferenceService))
      {
        var targetServiceType = binding?.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(scope.OwnerServiceType, targetServiceType, $"AsReference on '{el.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(targetServiceType, scope.OwnerServiceType);

        if (targetServiceType != null &&
            scope.Db != null &&
            (select.ReferenceLabelPaths == null || select.ReferenceLabelPaths.Count == 0 || select.ReferenceLabelPaths is ["name"]))
        {
          var customPaths = ServiceRegistry.GetReferenceLabelPaths(targetServiceType, scope.Db, scope.App);
          if (customPaths is { Count: > 0 })
          {
            select.ReferenceLabelPaths = [.. customPaths];
            ReferenceLookupAllowlist.RegisterLabels(targetServiceType, customPaths);
          }
        }
      }
      else if (binding is { IsRightsTree: true } && el.Model is FormCheckboxTreeModel treeModel)
      {
        var held = scope.User is { IsAuthenticated: true } ? scope.User.Permissions : null;
        treeModel.Nodes = RightsTree.Build(ModuleCatalog.Instance, held);
      }
    }
  }

  public static async Task<List<FormElementDto>> ProjectWireElementsAsync(
    ElementResolveScope scope,
    IReadOnlyList<FormElementDto> elements,
    string? inheritTimeZoneLabel = null,
    IReadOnlySet<string>? presetDisabledPaths = null,
    bool includeCreateOverlays = true,
    CancellationToken ct = default)
  {
    var l = scope.Localizer;
    var projected = new List<FormElementDto>(elements.Count);

    foreach (var el in elements)
    {
      var binding = FormElementBinding.TryGet(el);
      var isDisabled = el.IsDisabled || presetDisabledPaths != null && !string.IsNullOrEmpty(el.Path) && presetDisabledPaths.Contains(el.Path);
      var model = el.Model;

      if (model is FormSelectModel select)
      {
        var localizedOptions = ScreenBuilderHelpers.LocalizeOptions(l, select.Options);
        var createOverlay = select.CreateOverlay;

        if (includeCreateOverlays &&
            !string.IsNullOrEmpty(select.ReferenceService) &&
            binding is { AllowsCreate: true } &&
            !isDisabled &&
            !el.IsServerOwned &&
            !scope.IsReadOnly)
        {
          var targetServiceType = binding.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
          var createFormType = binding.CreateFormServiceType ?? ParentScopeValidator.ResolveConventionFormType(targetServiceType);
          if (createFormType != null && typeof(IFormService).IsAssignableFrom(createFormType))
          {
            ModuleCatalog.EnsureModuleServiceAccessAllowed(scope.OwnerServiceType, createFormType, $"AllowCreate on '{el.Path}'");
            var targetFeat = ModuleCatalog.Instance.FindFeature(createFormType)
              ?? (targetServiceType != null ? ModuleCatalog.Instance.FindFeature(targetServiceType) : null);
            var canCreate = targetFeat is { IsReadOnly: false } &&
                            scope.User is { IsAuthenticated: true } &&
                            ServiceEndpointsExtensions.CanEdit(targetFeat, scope.User);

            if (canCreate)
            {
              var createServicePath = ServicePathResolver.Resolve(createFormType);
              try
              {
                var rawFormInst = scope.Db != null
                  ? ModuleCatalog.Instance.CreateServiceInstance(createFormType, scope.Db, scope.App)
                  : ModuleCatalog.Instance.CreateServiceInstance(createFormType);
                if (rawFormInst is IFormService createFormInst)
                {
                  createOverlay = await createFormInst.OverlayAsync(createServicePath, ct);
                }
              }
              catch
              {
              }

              createOverlay ??= OverlayModel.Drawer($"{createServicePath}.modal").Screen(createServicePath).Build();
            }
          }
        }

        model = select with
        {
          Options = localizedOptions,
          CreateOverlay = createOverlay
        };
      }
      else if (binding is { IsRightsTree: true } && model is FormCheckboxTreeModel treeModel)
      {
        var held = scope.User is { IsAuthenticated: true } ? scope.User.Permissions : null;
        model = treeModel with { Nodes = RightsTree.Build(ModuleCatalog.Instance, held) };
      }
      else if (model is FormCheckboxSetModel checkboxSet)
      {
        model = checkboxSet with
        {
          Options = ScreenBuilderHelpers.LocalizeOptions(l, checkboxSet.Options) ?? []
        };
      }
      else if (model is FormTimeZoneModel tz && inheritTimeZoneLabel != null)
      {
        model = tz with { Inherit = inheritTimeZoneLabel };
      }

      var clone = CloneElement(
        el,
        label: !string.IsNullOrEmpty(el.Label)
          ? ScreenBuilderHelpers.ResolveFieldLabel(l, scope.Feature, el.Path, el.Label)
          : el.Label,
        title: !string.IsNullOrEmpty(el.Title) ? l[el.Title] : el.Title,
        separatorLabel: !string.IsNullOrEmpty(el.SeparatorLabel) ? l[el.SeparatorLabel] : el.SeparatorLabel,
        placeholder: !string.IsNullOrEmpty(el.Placeholder) ? l[el.Placeholder] : el.Placeholder,
        help: !string.IsNullOrEmpty(el.Help) ? l[el.Help] : el.Help,
        isDisabled: isDisabled,
        model: model);

      FormElementBinding.Link(el, clone);
      projected.Add(clone);
    }

    return projected;
  }

  public static FormElementDto CloneElement(
    FormElementDto source,
    string? label = null,
    string? title = null,
    string? separatorLabel = null,
    string? placeholder = null,
    string? help = null,
    bool? isDisabled = null,
    FormElementModel? model = null) =>
    new()
    {
      Path = source.Path,
      ReferencePath = source.ReferencePath,
      Label = label ?? source.Label,
      Title = title ?? source.Title,
      Type = source.Type,
      TemporalType = source.TemporalType,
      Model = model ?? source.Model,
      Span = source.Span,
      Tab = source.Tab,
      IsRequired = source.IsRequired,
      Constraints = source.Constraints,
      IsDisabled = isDisabled ?? source.IsDisabled,
      IsHidden = source.IsHidden,
      IsServerOwned = source.IsServerOwned,
      IsInputOnly = source.IsInputOnly,
      IsFocused = source.IsFocused,
      IsSecret = source.IsSecret,
      SavesOnChange = source.SavesOnChange,
      ValueKind = source.ValueKind,
      HasSeparatorAfter = source.HasSeparatorAfter,
      SeparatorLabel = separatorLabel ?? source.SeparatorLabel,
      Placeholder = placeholder ?? source.Placeholder,
      Help = help ?? source.Help,
      SelectPatches = source.SelectPatches,
      VisibleWhen = source.VisibleWhen,
      DisabledWhen = source.DisabledWhen
    };
}
