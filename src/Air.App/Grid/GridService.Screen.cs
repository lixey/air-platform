using Platform.App.Grid.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default)
  {
    ParentService = request?.ParentService;
    ParentId = request?.ParentId;
    if (!await EnsureParentScopeAsync(lockParent: false, ct))
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

    var columns = await CreateColumnsAsync(ct);

    var isReferenceSelect = request?.ReferenceLabelPaths is { Count: > 0 };
    var isPickerOrSelect = isReferenceSelect || (request?.IsReferencePicker ?? false);
    if (isReferenceSelect)
    {
      return BuildSelectScreen(servicePath, columns, request);
    }

    var formOverlay = await ResolveFormOverlayAsync(ct);
    var nav = await GetNavAsync(servicePath, ct);

    var projectedColumns = ProjectScreenColumns(columns.List);

    var parentChild = ResolveParentChildBuilder();
    var isUserReadOnly = User is { IsAuthenticated: true } && !ServiceEndpointsExtensions.CanEdit(Feature, User);
    var isGridReadOnly = IsReadOnly || isUserReadOnly || parentChild?.IsReadOnly == true;

    var navStart = new List<NavElementModel>();
    if (!isGridReadOnly && nav.ShowCreate)
    {
      if (nav.Creates.Count > 0)
      {
        foreach (var (createFormType, createModel) in nav.Creates)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), createFormType, "Nav.AddCreate");
          createModel.Overlay = await ResolveFormOverlayForTypeAsync(createFormType, createModel.Service!, ct);
          formOverlay ??= createModel.Overlay;
          navStart.Add(createModel);
        }
      }
      else if (formOverlay is not null)
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
    }

    if (!isGridReadOnly)
    {
      navStart.AddRange(nav.Pickers);
    }

    navStart.AddRange(nav.Start);

    var actions = await BuildActionsAsync(ct);

    var isParentReadOnly = parentChild?.IsReadOnly == true;
    var permittedActions = new List<(GridNavActionModel Model, bool IsInMenu)>();
    var declaredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      declaredKeys.Add(entry.Key);
      if (entry.Permission.IsPermitted(Feature, User, entry.Handle, isGridReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
      {
        var model = await entry.BuildModelAsync(GetType(), Feature, servicePath, db, PlatformApp, User, L, ct);
        permittedActions.Add((model, entry.IsInMenu));
      }
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        var inherited = await form.BuildInheritedGridActionsAsync(servicePath, ParentId, ParentService, isGridReadOnly, isParentReadOnly, ct);
        foreach (var item in inherited)
        {
          if (declaredKeys.Add(item.Model.Key))
          {
            permittedActions.Add((item.Model, item.IsInMenu));
          }
        }
      }
    }

    var navElements = new List<NavElementModel>();
    foreach (var (model, _) in permittedActions.Where(a => !a.Model.NeedsSelection))
    {
      navElements.Add(model);
    }

    if (nav.ShowSearch)
    {
      navElements.Add(new GridNavSearchModel { Key = "search", Label = L["Search"] });
    }

    if (nav.ShowFilters)
    {
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
    }

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
          IsInline = group.IsInline,
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

    if (nav.Elements.Count > 0)
    {
      navElements.AddRange(nav.Elements);
    }

    var actionElements = new List<NavElementModel>();
    if (columns.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      var statusButton = await BuildStatusNavElementAsync(
        servicePath,
        statusFlow,
        IsServiceLockedReadOnly,
        columns.StatusFlowAllowsReadOnly,
        isParentReadOnly,
        ct);
      if (statusButton != null)
      {
        actionElements.Add(statusButton);
      }
    }

    ScreenBuilderHelpers.AppendMenuAndInlineActions(
      actionElements,
      permittedActions.Select(a => ((NavElementModel)a.Model, a.IsInMenu)),
      L);

    if (nav.Selection.Count > 0)
    {
      actionElements.AddRange(nav.Selection);
    }

    var canExport = nav.ShowExport && User is { IsAuthenticated: true } && ServiceEndpointsExtensions.CanExport(Feature, User);
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
    if (nav.ShowDelete && !isGridReadOnly && CanDelete())
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

    var navEnd = new List<NavElementModel>(nav.End);
    if (ParentId is > 0 && !string.IsNullOrEmpty(ParentService))
    {
      navEnd.Add(new NavCloseModel
      {
        Key = "close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Ghost,
        Title = L["Close"]
      });
    }

    var navMore = new List<NavElementModel>(nav.More);
    if (canExport)
    {
      navMore.Add(new GridNavActionModel
      {
        Key = "export",
        Service = servicePath,
        Method = "export",
        NeedsSelection = false,
        IsDownload = true,
        Color = UiColor.Green,
        Icon = UiIcon.Excel,
        Label = L["Export"]
      });
    }

    var topNav = new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd,
      More = navMore
    };

    var actionNav = new NavModel
    {
      Key = $"{servicePath}.actions",
      Start = [],
      Elements = actionElements,
      End = actionEnd,
      More = []
    };

    foreach (var customize in nav.Customizers)
    {
      customize(topNav, actionNav);
    }

    FilterNode? initialFilter = null;
    if (!isPickerOrSelect && queryDeclaration.InitialFilters.Count > 0)
    {
      var projectedInitialRules = new List<FilterNode>(queryDeclaration.InitialFilters.Count);
      foreach (var rule in queryDeclaration.InitialFilters)
      {
        var picked = rule.Picked;
        if (picked is null)
        {
          var col = columns.List.FirstOrDefault(c =>
            string.Equals(c.Path, rule.Field, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.FilterPath, rule.Field, StringComparison.OrdinalIgnoreCase));
          if (col?.Options is { Count: > 0 } opts)
          {
            if (rule.Value is IEnumerable enumerable and not string)
            {
              picked = enumerable.Cast<object?>()
                .Where(v => v != null)
                .Select(v =>
                {
                  var id = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                  var opt = opts.FirstOrDefault(o => o.Id == id);
                  return new PickedValue
                  {
                    Id = id,
                    Label = opt is { Name.Length: > 0 } ? L[opt.Name] : id.ToString(CultureInfo.InvariantCulture)
                  };
                })
                .ToList();
            }
            else if (rule.Value is not null)
            {
              var id = Convert.ToInt64(rule.Value, CultureInfo.InvariantCulture);
              var opt = opts.FirstOrDefault(o => o.Id == id);
              picked =
              [
                new PickedValue
                {
                  Id = id,
                  Label = opt is { Name.Length: > 0 } ? L[opt.Name] : id.ToString(CultureInfo.InvariantCulture)
                }
              ];
            }
          }
        }

        projectedInitialRules.Add(ReferenceEquals(picked, rule.Picked)
          ? rule
          : new FilterRule
          {
            Field = rule.Field,
            Operator = rule.Operator,
            Value = rule.Value,
            Picked = picked
          });
      }

      initialFilter = projectedInitialRules.Count == 1
        ? projectedInitialRules[0]
        : FilterNode.All([.. projectedInitialRules]);
    }

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = projectedColumns,
      PageSize = queryDeclaration.CustomPageSize ?? 200,
      IsReadOnly = isGridReadOnly,
      Form = formOverlay,
      InitialFilter = initialFilter,
      MutationTexts = ScreenBuilderHelpers.BuildMutationTexts(L),
      Nav = topNav,
      ActionNav = actionNav
    };

    var layout = PanelLayout.Row(servicePath)
      .Panel("main", gridModel)
      .Build();

    return new ScreenDto { Layout = layout };
  }

  private ScreenDto BuildSelectScreen(
    string servicePath,
    Columns<TEntity, TRow> columns,
    GridRequest? request)
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

    var projectedMatching = ProjectScreenColumns(matching);

    var selectGridModel = new GridModel
    {
      Service = servicePath,
      Columns = projectedMatching,
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

  private List<GridColumnDto> ProjectScreenColumns(IEnumerable<GridColumnDto> columns)
  {
    var gridType = GetType();
    var dbModel = db.Model;
    var projected = new List<GridColumnDto>();

    foreach (var col in columns)
    {
      var runtime = GridColumnRuntime.TryGet(col);
      if (!string.IsNullOrEmpty(col.ReferenceService))
      {
        var refServiceType = runtime?.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(col.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, refServiceType, $"AsReference on '{col.Path}'");
      }

      if (col.Cell is GridCellPickerModel cellPicker && !string.IsNullOrEmpty(cellPicker.ReferenceService))
      {
        var pickerServiceType = runtime?.PickerSourceType ?? ModuleCatalog.Instance.FindServiceType(cellPicker.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, pickerServiceType, $"PickedBy on '{col.Path}'");
      }

      var isRequired = ResolveColumnRequired(col, runtime, dbModel);

      var localizedOptions = ScreenBuilderHelpers.LocalizeOptions(L, col.Options);
      var operators = col.Operators;
      if (col.IsFilterable && operators == null)
      {
        var isRefOrSelect = col.ReferenceService != null || localizedOptions is { Count: > 0 } || col.Cell is GridCellReferenceModel;
        operators = GridFilterOperators.ForField(col.Type, isRefOrSelect, col.TemporalType != null);
      }

      var header = col.Header;
      if (col.IsFilterable && header == null)
      {
        if (col.Type is UiFieldType.Date or UiFieldType.DateTime)
        {
          header = new GridHeaderDateModel();
        }
        else if (col.Type == UiFieldType.Checkbox)
        {
          header = new GridHeaderCheckboxModel();
        }
      }

      var clone = new GridColumnDto
      {
        Path = col.Path,
        FilterPath = col.FilterPath,
        Label = !string.IsNullOrEmpty(col.Label) ? ScreenBuilderHelpers.ResolveFieldLabel(L, Feature, col.Path, col.Label) : col.Label,
        Title = !string.IsNullOrEmpty(col.Title) ? L[col.Title] : col.Title,
        Type = col.Type,
        TemporalType = col.TemporalType,
        Width = col.Width,
        MaxWidth = col.MaxWidth,
        IsSortable = col.IsSortable,
        IsFilterable = col.IsFilterable,
        Operators = operators,
        IsResizable = col.IsResizable,
        IsHidden = col.IsHidden,
        IsDisabled = col.IsDisabled,
        IsRequired = isRequired,
        IsSticky = col.IsSticky,
        IsStickyRight = col.IsStickyRight,
        IsWrapped = col.IsWrapped,
        IsEditable = col.IsEditable,
        ConfirmsEdit = col.ConfirmsEdit,
        ConfirmText = !string.IsNullOrEmpty(col.ConfirmText) ? L[col.ConfirmText] : col.ConfirmText,
        ReferenceService = col.ReferenceService,
        ReferenceLabelPaths = col.ReferenceLabelPaths,
        ReferenceFilter = col.ReferenceFilter,
        NumberEdit = col.NumberEdit,
        Options = localizedOptions,
        Cell = col.Cell,
        Header = header,
        SelectionSummary = col.SelectionSummary
      };

      GridColumnRuntime.Link(col, clone);
      projected.Add(clone);
    }

    return projected;
  }

  private static bool ResolveColumnRequired(
    GridColumnDto col,
    GridColumnRuntime? runtime,
    Microsoft.EntityFrameworkCore.Metadata.IModel dbModel)
  {
    if (runtime?.ExplicitRequired is { } explicitRequired)
    {
      return explicitRequired;
    }

    if (runtime?.FilterExpression is { } filterExpr)
    {
      var retType = filterExpr.ReturnType;
      var isCollection = retType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(retType);
      return !isCollection && retType.IsValueType && Nullable.GetUnderlyingType(retType) is null;
    }

    if (runtime?.ValueExpression is not null)
    {
      return false;
    }

    if (string.IsNullOrEmpty(col.Path))
    {
      return col.IsRequired;
    }

    if (ColumnRequiredResolver.Resolve(typeof(TEntity), col.Path, dbModel) is { } entityRequired)
    {
      return entityRequired;
    }

    return typeof(TRow) != typeof(TEntity) && (ColumnRequiredResolver.Resolve(typeof(TRow), col.Path, dbModel) ?? false);
  }

  internal async Task<OverlayDto> ResolveFormOverlayForTypeAsync(
    Type? formType,
    string formServicePath,
    CancellationToken ct = default)
  {
    if (formType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), formType, "ResolveFormOverlay");
    }

    if (State.CachedFormOverlays.TryGetValue(formServicePath, out var cachedOverlay))
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
          State.CachedFormOverlays[formServicePath] = resolved;
          return resolved;
        }
      }
      catch
      {
      }
    }

    var fallback = OverlayModel.Drawer($"{formServicePath}.modal").Screen(formServicePath).Build();
    State.CachedFormOverlays[formServicePath] = fallback;
    return fallback;
  }

  private async Task<GridNavStatusModel?> BuildStatusNavElementAsync(
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    bool isGridReadOnly,
    bool allowReadOnly,
    bool isParentReadOnly,
    CancellationToken ct)
  {
    (statusFlow as IPlatformService)?.BindContext(PlatformApp);

    var isReadOnlyEntity = typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity));
    var statusPath = StatusMoveNavBuilder.ResolveStatusPath(statusFlow.StatusMember);
    var statusMoves = new List<GridNavStatusMoveDto>();

    foreach (var t in statusFlow.Transitions)
    {
      if (!StatusMoveNavBuilder.CanMoveOnScreen(
            Feature,
            User,
            t,
            isGridReadOnly,
            allowReadOnly,
            isParentReadOnly,
            isReadOnlyEntity))
      {
        continue;
      }

      var meta = await StatusMoveNavBuilder.BuildTransitionMetaAsync(GetType(), Feature, db, PlatformApp, User, L, statusFlow, t, ct);
      statusMoves.Add(new GridNavStatusMoveDto
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
      });
    }

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
