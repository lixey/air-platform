using Platform.App.Grid.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default)
  {
    ParentService = request?.ParentService;
    ParentId = request?.ParentId;
    if (!await EnsureParentScopeAsync(lockParent: false, ct) && ParentId is > 0)
    {
      return new ScreenDto
      {
        Data = new GridPageDto<object>
        {
          Rows = [],
          Total = 0,
          Number = request?.Page ?? 1,
          Size = request?.PageSize ?? 50,
          HasMore = false
        }
      };
    }

    var columns = CreateColumns();

    var isReferenceSelect = request?.ReferenceLabelPaths is { Count: > 0 };
    var isPickerOrSelect = isReferenceSelect || (request?.IsReferencePicker ?? false);
    if (isReferenceSelect)
    {
      return BuildSelectScreen(servicePath, columns, request);
    }

    var formOverlay = await ResolveFormOverlayAsync(columns, ct);

    var nav = GetNav(servicePath);

    LocalizeAndConfigureColumns(columns.List);

    var parentChild = ResolveParentChildBuilder();
    var isUserReadOnly = User is { IsAuthenticated: true } && !ServiceEndpointsExtensions.CanEdit(Feature, User);
    var isGridReadOnly = IsReadOnly || isUserReadOnly || parentChild?.IsReadOnly == true;

    var navStart = new List<NavElementModel>();
    if (!isGridReadOnly && nav.Creates.Count > 0)
    {
      foreach (var (createFormType, createModel) in nav.Creates)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), createFormType, "Nav.AddCreate");
        createModel.Overlay = await ResolveFormOverlayForTypeAsync(createFormType, createModel.Service!, ct);
        formOverlay ??= createModel.Overlay;
        navStart.Add(createModel);
      }
    }
    else if (formOverlay is not null && !isGridReadOnly)
    {
      navStart.Add(new GridNavAddModel
      {
        Key = "add",
        Label = L["Add"],
        Variant = ButtonVariant.Soft,
        Color = UiColor.Orange,
        Icon = UiIcon.Plus
      });
    }

    if (!isGridReadOnly)
    {
      navStart.AddRange(nav.Pickers);
    }

    navStart.AddRange(nav.CustomStart);

    var permittedActions = new List<NavActionEntry>();
    foreach (var entry in nav.Actions)
    {
      if (ScreenBuilderHelpers.IsActionPermitted(entry.Method.Method, Feature, User, parentChild?.IsReadOnly == true, entry.IsAllowedInReadOnly))
      {
        if (entry.Ask != null)
        {
          entry.Model.Ask = ScreenBuilderHelpers.BuildActionAsk(entry.Model, entry.Ask, entry.DefaultProvider != null, L);
          entry.Model.Confirm = null;
        }

        permittedActions.Add(entry);
      }
    }

    var navElements = new List<NavElementModel>();
    foreach (var entry in permittedActions.Where(a => !a.Model.NeedsSelection))
    {
      navElements.Add(entry.Model);
    }

    navElements.Add(new GridNavSearchModel { Key = "search", Label = L["Search"] });
    navElements.Add(new NavOverlayModel
    {
      Key = "filters",
      Label = L["Filters"],
      Icon = UiIcon.Filter,
      Color = UiColor.Emerald,
      Variant = ButtonVariant.Soft,
      Overlay = OverlayModel.Drawer(
          $"{servicePath}.filters",
          PanelLayout.Row($"{servicePath}.filters")
            .Panel("filters", new GridFiltersOverlayModel())
            .Build())
        .Width(FiltersWidth)
        .Build()
    });

    var queryDeclaration = GetQueryDeclaration();
    if (!isPickerOrSelect)
    {
      foreach (var group in queryDeclaration.NamedFilterGroups)
      {
        var chosenKey = request?.NamedFilters?.FirstOrDefault(k =>
            group.Items.Any(i => string.Equals(i.Key, k, StringComparison.OrdinalIgnoreCase)))
          ?? group.Items.FirstOrDefault(i => i.IsDefault)?.Key;
        var groupItems = new List<GridNavNamedFilterModel>(group.Items.Count);
        foreach (var item in group.Items)
        {
          groupItems.Add(new GridNavNamedFilterModel
          {
            Key = item.Key,
            Label = L[item.Label],
            Title = item.Title != null ? L[item.Title] : null,
            Color = item.Color,
            IsSelected = string.Equals(item.Key, chosenKey, StringComparison.OrdinalIgnoreCase)
          });
        }

        navElements.Add(new GridNavNamedFilterGroupModel
        {
          Key = group.Key,
          Label = group.Label != null ? L[group.Label] : null,
          IsRemembered = group.IsRemembered,
          Items = groupItems
        });
      }

      foreach (var item in queryDeclaration.NamedFilters)
      {
        navElements.Add(new GridNavNamedFilterModel
        {
          Key = item.Key,
          Label = L[item.Label],
          Title = item.Title != null ? L[item.Title] : null,
          Color = item.Color,
          IsRemembered = item.IsRemembered,
          IsSelected = request?.NamedFilters?.Any(k => string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)) == true
        });
      }
    }

    var actionElements = new List<NavElementModel>();
    if (columns.StatusFlow is IRecordStatuses<TEntity> statusFlow && (!isGridReadOnly || columns.StatusFlowAllowsReadOnly))
    {
      var statusButton = BuildStatusNavElement(servicePath, statusFlow);
      if (statusButton != null)
      {
        actionElements.Add(statusButton);
      }
    }

    ScreenBuilderHelpers.AppendMenuAndInlineActions(
      actionElements,
      permittedActions.Select(a => ((NavElementModel)a.Model, a.IsInMenu)),
      L);

    var canExport = User is { IsAuthenticated: true } && ServiceEndpointsExtensions.CanExport(Feature, User);
    if (canExport)
    {
      actionElements.Add(new GridNavActionModel
      {
        Key = "export",
        Service = servicePath,
        Method = "export",
        NeedsSelection = true,
        IsDownload = true,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft,
        Icon = UiIcon.Excel,
        Label = L["Export"]
      });
    }

    var actionEnd = new List<NavElementModel>();
    if (!isGridReadOnly && CanDelete())
    {
      actionEnd.Add(new GridNavActionModel
      {
        Key = "delete",
        Service = servicePath,
        Method = "delete",
        NeedsSelection = true,
        IsSingle = !GetHooks().IsMultiDeleteAllowed,
        Color = UiColor.Red,
        Variant = ButtonVariant.Soft,
        Icon = UiIcon.Trash,
        Label = L["Delete"],
        Confirm = new OverlayConfirmModel { Message = L["DeleteConfirm"], Color = UiColor.Red }
      });
    }

    FilterNode? initialFilter = null;
    if (!isPickerOrSelect)
    {
      foreach (var rule in queryDeclaration.InitialFilters)
      {
        if (rule.Picked is null)
        {
          var col = columns.List.FirstOrDefault(c =>
            string.Equals(c.Path, rule.Field, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.FilterPath, rule.Field, StringComparison.OrdinalIgnoreCase));
          if (col?.Options is { Count: > 0 } opts)
          {
            if (rule.Value is IEnumerable enumerable and not string)
            {
              rule.Picked = enumerable.Cast<object?>()
                .Where(v => v != null)
                .Select(v =>
                {
                  var id = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                  var opt = opts.FirstOrDefault(o => o.Id == id);
                  return new PickedValue { Id = id, Label = opt?.Name ?? id.ToString(CultureInfo.InvariantCulture) };
                })
                .ToList();
            }
            else if (rule.Value is not null)
            {
              var id = Convert.ToInt64(rule.Value, CultureInfo.InvariantCulture);
              var opt = opts.FirstOrDefault(o => o.Id == id);
              rule.Picked = [new PickedValue { Id = id, Label = opt?.Name ?? id.ToString(CultureInfo.InvariantCulture) }];
            }
          }
        }
      }

      initialFilter = queryDeclaration.InitialFilters.Count == 1
        ? queryDeclaration.InitialFilters[0]
        : queryDeclaration.InitialFilters.Count > 1
          ? FilterNode.All([.. queryDeclaration.InitialFilters.Cast<FilterNode>()])
          : null;
    }

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = columns.List.ToList(),
      PageSize = queryDeclaration.CustomPageSize ?? 200,
      IsReadOnly = isGridReadOnly,
      Form = formOverlay,
      InitialFilter = initialFilter,
      MutationTexts = ScreenBuilderHelpers.BuildMutationTexts(L),
      Nav = new NavModel
      {
        Key = $"{servicePath}.nav",
        Start = navStart,
        Elements = navElements,
        End = ParentId is > 0 && !string.IsNullOrEmpty(ParentService)
          ? [
              new NavCloseModel
              {
                Key = "close",
                Icon = UiIcon.Close,
                Color = UiColor.Slate,
                Variant = ButtonVariant.Ghost,
                Title = L["Close"]
              }
            ]
          : [],
        More = canExport
          ? [
              new GridNavActionModel
              {
                Key = "export",
                Service = servicePath,
                Method = "export",
                NeedsSelection = false,
                IsDownload = true,
                Color = UiColor.Green,
                Icon = UiIcon.Excel,
                Label = L["Export"]
              }
            ]
          : []
      },
      ActionNav = new NavModel
      {
        Key = $"{servicePath}.actions",
        Start = [],
        Elements = actionElements,
        End = actionEnd,
        More = []
      }
    };

    var layout = PanelLayout.Row(servicePath)
      .Panel("main", gridModel)
      .Build();

    return new ScreenDto { Layout = layout };
  }

  private ScreenDto BuildSelectScreen(string servicePath, Columns columns, GridRequest? request)
  {
    var labelPaths = request?.ReferenceLabelPaths is { Count: > 0 }
      ? request.ReferenceLabelPaths
      : ["name"];

    var patchColumns = request?.PatchSourceColumns ?? [];

    var matching = columns.List.Where(c =>
      labelPaths.Any(lp => string.Equals(c.Path, lp, StringComparison.OrdinalIgnoreCase)) ||
      patchColumns.Any(pc => string.Equals(c.Path, pc, StringComparison.OrdinalIgnoreCase))).ToList();

    if (matching.Count == 0)
    {
      var fallback = columns.List.FirstOrDefault(c => !string.Equals(c.Path, "id", StringComparison.OrdinalIgnoreCase))
        ?? columns.List.FirstOrDefault();
      if (fallback != null)
      {
        matching.Add(fallback);
      }
    }

    LocalizeAndConfigureColumns(matching);

    var selectGridModel = new GridModel
    {
      Service = servicePath,
      Columns = matching,
      IsReadOnly = true,
      Form = new OverlayDto(),
      Nav = new NavModel
      {
        Key = $"{servicePath}.select.nav",
        Elements = [new GridNavSearchModel { Key = "search", Label = L["Search"] }]
      },
      ActionNav = new NavModel { Key = $"{servicePath}.select.actions" }
    };

    var selectLayout = PanelLayout.Row(servicePath)
      .Panel("main", selectGridModel)
      .Build();

    return new ScreenDto { Layout = selectLayout };
  }

  private void LocalizeAndConfigureColumns(IEnumerable<GridColumnDto> columns)
  {
    foreach (var col in columns)
    {
      if (!string.IsNullOrEmpty(col.Label))
      {
        col.Label = ScreenBuilderHelpers.ResolveFieldLabel(L, Feature, col.Path, col.Label);
      }

      if (!string.IsNullOrEmpty(col.Title))
      {
        col.Title = L[col.Title];
      }

      col.Options = ScreenBuilderHelpers.LocalizeOptions(L, col.Options);
      if (!string.IsNullOrEmpty(col.ReferenceService))
      {
        var refServiceType = ModuleCatalog.Instance.FindServiceType(col.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), refServiceType, $"AsReference on '{col.Path}'");
      }

      if (col.Cell is GridCellPickerModel cellPicker && !string.IsNullOrEmpty(cellPicker.ReferenceService))
      {
        var pickerServiceType = col.PickerSourceType ?? ModuleCatalog.Instance.FindServiceType(cellPicker.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), pickerServiceType, $"PickedBy on '{col.Path}'");
      }

      if (col.ExplicitRequired.HasValue)
      {
        col.IsRequired = col.ExplicitRequired.Value;
      }
      else if (col.FilterExpression is not null)
      {
        var retType = col.FilterExpression.ReturnType;
        var isCollection = retType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(retType);
        col.IsRequired = !isCollection && retType.IsValueType && Nullable.GetUnderlyingType(retType) is null;
      }
      else if (col.ValueExpression is not null)
      {
        col.IsRequired = false;
      }
      else if (!string.IsNullOrEmpty(col.Path))
      {
        col.IsRequired = ColumnRequiredResolver.Resolve(typeof(TEntity), col.Path, db.Model)
          ?? (typeof(TRow) != typeof(TEntity) ? ColumnRequiredResolver.Resolve(typeof(TRow), col.Path, db.Model) ?? false : false);
      }

      if (col.IsFilterable && col.Operators == null)
      {
        var isRefOrSelect = col.ReferenceService != null || col.Options is { Count: > 0 } || col.Cell is GridCellReferenceModel;
        col.Operators = GridFilterOperators.ForField(col.Type, isRefOrSelect, col.TemporalType != null);
      }

      if (col.IsFilterable && col.Header == null)
      {
        if (col.Type is UiFieldType.Date or UiFieldType.Datetime)
        {
          col.Header = new GridHeaderDateModel();
        }
        else if (col.Type == UiFieldType.Checkbox)
        {
          col.Header = new GridHeaderCheckboxModel();
        }
      }
    }
  }

  protected virtual OverlayDto? ResolveFormOverlay(Columns columns) => null;

  protected virtual async Task<OverlayDto?> ResolveFormOverlayAsync(Columns columns, CancellationToken ct = default)
  {
    if (ResolveFormOverlay(columns) is { } customOverlay)
    {
      return customOverlay;
    }

    var detectedFormService = ResolveFormServicePath(columns);
    if (detectedFormService is null)
    {
      return null;
    }

    var formType = ResolveFormServiceType(columns) ?? ModuleCatalog.Instance.FindServiceType(detectedFormService);
    return await ResolveFormOverlayForTypeAsync(formType, detectedFormService, ct);
  }

  private async Task<OverlayDto> ResolveFormOverlayForTypeAsync(Type? formType, string formServicePath, CancellationToken ct = default)
  {
    if (formType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), formType, nameof(ResolveFormOverlay));
    }

    if (_cachedFormOverlays.TryGetValue(formServicePath, out var cachedOverlay))
    {
      return cachedOverlay;
    }

    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService formInstance)
        {
          var resolved = await formInstance.OverlayAsync(formServicePath, ct);
          _cachedFormOverlays[formServicePath] = resolved;
          return resolved;
        }
      }
      catch
      {
        // Fallback to default dialog overlay below
      }
    }

    var fallback = OverlayModel.Dialog($"{formServicePath}.modal").Screen(formServicePath).Build();
    _cachedFormOverlays[formServicePath] = fallback;
    return fallback;
  }

  private GridNavStatusModel? BuildStatusNavElement(string servicePath, IRecordStatuses<TEntity> statusFlow)
  {
    if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(PlatformApp);
    }

    var statusPath = StatusMoveNavBuilder.ResolveStatusPath(statusFlow.StatusMember);

    var statusMoves = statusFlow.Transitions
      .Where(t => StatusMoveNavBuilder.CanMove(Feature, User, t.Name))
      .Select(t =>
      {
        var meta = StatusMoveNavBuilder.BuildTransitionMeta(Feature, L, statusFlow, t);
        return new GridNavStatusMoveDto
        {
          From = [.. t.From],
          Action = new GridNavActionModel
          {
            Key = meta.Key,
            Label = meta.Label,
            Title = meta.Title,
            Color = meta.Color,
            Service = servicePath,
            Method = meta.Method,
            NeedsSelection = true,
            Confirm = meta.Confirm,
            Ask = meta.Ask,
            UnavailableReasons = meta.UnavailableReasons
          }
        };
      }).ToList();

    if (statusMoves.Count == 0)
    {
      return null;
    }

    return new GridNavStatusModel
    {
      Key = "status",
      Label = L["Status"],
      EmptyTitle = L["StatusNoMove"],
      Path = statusPath,
      Moves = statusMoves
    };
  }
}
