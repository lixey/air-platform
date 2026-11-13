using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form.Internals;

internal static class ElementResolver
{
  public static async Task ResolveAsync<TEntity>(
    ElementResolveScope scope,
    Elements elements,
    CancellationToken ct = default)
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

    var efEntityType = scope.Db.Model.FindEntityType(typeof(TEntity));
    FormConstraintExtractor.ApplyElementConstraints(typeof(TEntity), efEntityType, elements.List);

    var l = scope.Localizer;
    foreach (var el in elements.List)
    {
      if (!string.IsNullOrEmpty(el.SeparatorLabel))
      {
        el.SeparatorLabel = l[el.SeparatorLabel];
      }

      if (!string.IsNullOrEmpty(el.Label))
      {
        el.Label = ScreenBuilderHelpers.ResolveFieldLabel(l, scope.Feature, el.Path, el.Label);
      }

      if (!string.IsNullOrEmpty(el.Title))
      {
        el.Title = l[el.Title];
      }

      if (!string.IsNullOrEmpty(el.Placeholder))
      {
        el.Placeholder = l[el.Placeholder];
      }

      if (!string.IsNullOrEmpty(el.Help))
      {
        el.Help = l[el.Help];
      }

      if (el.Model is FormSelectModel select)
      {
        select.Options = ScreenBuilderHelpers.LocalizeOptions(l, select.Options);

        if (!string.IsNullOrEmpty(select.ReferenceService))
        {
          var targetServiceType = el.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
          ModuleCatalog.EnsureModuleServiceAccessAllowed(scope.OwnerServiceType, targetServiceType, $"AsReference on '{el.Path}'");
          ReferenceLookupAllowlist.RegisterReferrer(targetServiceType, scope.OwnerServiceType);

          if (targetServiceType != null &&
              (select.ReferenceLabelPaths == null || select.ReferenceLabelPaths.Count == 0 || select.ReferenceLabelPaths is ["name"]))
          {
            var customPaths = ServiceRegistry.GetReferenceLabelPaths(targetServiceType, scope.Db, scope.App);
            if (customPaths is { Count: > 0 })
            {
              select.ReferenceLabelPaths = [.. customPaths];
              ReferenceLookupAllowlist.RegisterLabels(targetServiceType, customPaths);
            }
          }

          if (el.AllowsCreate && !el.IsDisabled && !el.IsServerOwned && !scope.IsReadOnly)
          {
            var createFormType = el.CreateFormServiceType ?? ParentScopeValidator.ResolveConventionFormType(targetServiceType);
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
                  if (ModuleCatalog.Instance.CreateServiceInstance(createFormType, scope.Db, scope.App) is IFormService createFormInst)
                  {
                    select.CreateOverlay = await createFormInst.OverlayAsync(createServicePath, ct);
                  }
                }
                catch
                {
                }

                select.CreateOverlay ??= OverlayModel.Drawer($"{createServicePath}.modal").Screen(createServicePath).Build();
              }
            }
          }
        }
      }
      else if (el.IsRightsTree && el.Model is FormCheckboxTreeModel treeModel)
      {
        var held = scope.User is { IsAuthenticated: true } ? scope.User.Permissions : null;
        treeModel.Nodes = RightsTree.Build(ModuleCatalog.Instance, held);
      }
    }
  }
}
