using Platform.App.Screens.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridScreenBuilder
{
  public static async Task<ScreenDto> BuildScreenAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string servicePath,
    GridRequest? request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request?.ParentService;
    grid.ParentId = request?.ParentId;
    if (!await grid.EnsureParentScopeInternalAsync(lockParent: false, ct))
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

    var columns = grid.CreateColumnsDeclaration();

    var isReferenceSelect = request?.ReferenceLabelPaths is { Count: > 0 };
    var isPickerOrSelect = isReferenceSelect || (request?.IsReferencePicker ?? false);
    if (isReferenceSelect)
    {
      return BuildSelectScreen(grid, servicePath, columns, request);
    }

    var formOverlay = await grid.InvokeResolveFormOverlayAsync(columns, ct);

    var nav = grid.GetNavDeclaration(servicePath);

    LocalizeAndConfigureColumns(grid, columns.List);

    var parentChild = grid.ResolveParentChildBuilder();
    var isUserReadOnly = grid.User is { IsAuthenticated: true } && !ServiceEndpointsExtensions.CanEdit(grid.Feature, grid.User);
    var isGridReadOnly = grid.IsGridReadOnly || isUserReadOnly || parentChild?.IsReadOnly == true;

    var navStart = new List<NavElementModel>();
    if (!isGridReadOnly && nav.Creates.Count > 0)
    {
      foreach (var (createFormType, createModel) in nav.Creates)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(grid.GetType(), createFormType, "Nav.AddCreate");
        createModel.Overlay = await ResolveFormOverlayForTypeAsync(grid, createFormType, createModel.Service!, ct);
        formOverlay ??= createModel.Overlay;
        navStart.Add(createModel);
      }
    }
    else if (formOverlay is not null && !isGridReadOnly)
    {
      navStart.Add(new GridNavAddModel
      {
        Key = "add",
        Label = grid.L["Add"],
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

    var actions = new GridService<TEntity, TRow>.Actions();
    await grid.InvokeSetActionsAsync(actions, ct);

    var isParentReadOnly = parentChild?.IsReadOnly == true;
    var permittedActions = new List<(GridNavActionModel Model, bool IsInMenu)>();
    var declaredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      declaredKeys.Add(entry.Key);
      if (entry.Permission.IsPermitted(grid.Feature, grid.User, entry.Handle, isGridReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
      {
        var model = await entry.BuildModelAsync(grid.GetType(), grid.Feature, servicePath, grid.DbContext, grid.PlatformApp, grid.User, grid.L, ct);
        permittedActions.Add((model, entry.IsInMenu));
      }
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, grid.DbContext, grid.PlatformApp) is IFormService form)
      {
        if (grid.PlatformApp != null)
        {
          form.AttachPlatformApp(grid.PlatformApp);
        }

        var inherited = await form.BuildInheritedGridActionsAsync(servicePath, grid.ParentId, grid.ParentService, isGridReadOnly, isParentReadOnly, ct);
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

    navElements.Add(new GridNavSearchModel { Key = "search", Label = grid.L["Search"] });
    navElements.Add(new NavOverlayModel
    {
      Key = "filters",
      Label = grid.L["Filters"],
      Icon = UiIcon.Filter,
      Color = UiColor.Emerald,
      Variant = ButtonVariant.Soft,
      Overlay = OverlayModel.Drawer(
          $"{servicePath}.filters",
          PanelLayout.Row($"{servicePath}.filters")
            .Panel("filters", new GridFiltersOverlayModel())
            .Build())
        .Width(grid.GridFiltersWidth)
        .Build()
    });

    var queryDeclaration = grid.GetQueryDeclaration();
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
            Label = grid.L[item.Label],
            Title = item.Title != null ? grid.L[item.Title] : null,
            Color = item.Color,
            IsSelected = string.Equals(item.Key, chosenKey, StringComparison.OrdinalIgnoreCase)
          });
        }

        navElements.Add(new GridNavNamedFilterGroupModel
        {
          Key = group.Key,
          Label = group.Label != null ? grid.L[group.Label] : null,
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
          Label = grid.L[item.Label],
          Title = item.Title != null ? grid.L[item.Title] : null,
          Color = item.Color,
          IsRemembered = item.IsRemembered,
          IsSelected = request?.NamedFilters?.Any(k => string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)) == true
        });
      }
    }

    var actionElements = new List<NavElementModel>();
    if (columns.StatusFlow is IRecordStatuses<TEntity> statusFlow && (!isGridReadOnly || columns.StatusFlowAllowsReadOnly))
    {
      var statusButton = await BuildStatusNavElementAsync(grid, servicePath, statusFlow, ct);
      if (statusButton != null)
      {
        actionElements.Add(statusButton);
      }
    }

    ScreenBuilderHelpers.AppendMenuAndInlineActions(
      actionElements,
      permittedActions.Select(a => ((NavElementModel)a.Model, a.IsInMenu)),
      grid.L);

    var canExport = grid.User is { IsAuthenticated: true } && ServiceEndpointsExtensions.CanExport(grid.Feature, grid.User);
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
        Label = grid.L["Export"]
      });
    }

    var actionEnd = new List<NavElementModel>();
    if (!isGridReadOnly && grid.CanDelete())
    {
      actionEnd.Add(new GridNavActionModel
      {
        Key = "delete",
        Service = servicePath,
        Method = "delete",
        NeedsSelection = true,
        IsSingle = !grid.GetHooksDeclaration().IsMultiDeleteAllowed,
        Color = UiColor.Red,
        Variant = ButtonVariant.Soft,
        Icon = UiIcon.Trash,
        Label = grid.L["Delete"],
        Confirm = new OverlayConfirmModel { Message = grid.L["DeleteConfirm"], Color = UiColor.Red }
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
      MutationTexts = ScreenBuilderHelpers.BuildMutationTexts(grid.L),
      Nav = new NavModel
      {
        Key = $"{servicePath}.nav",
        Start = navStart,
        Elements = navElements,
        End = grid.ParentId is > 0 && !string.IsNullOrEmpty(grid.ParentService)
          ? [
              new NavCloseModel
              {
                Key = "close",
                Icon = UiIcon.Close,
                Color = UiColor.Slate,
                Variant = ButtonVariant.Ghost,
                Title = grid.L["Close"]
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
                Label = grid.L["Export"]
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

  private static ScreenDto BuildSelectScreen<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string servicePath,
    GridService<TEntity, TRow>.Columns columns,
    GridRequest? request)
    where TEntity : class, IEntity, new()
    where TRow : class
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

    LocalizeAndConfigureColumns(grid, matching);

    var selectGridModel = new GridModel
    {
      Service = servicePath,
      Columns = matching,
      IsReadOnly = true,
      Form = new OverlayDto(),
      Nav = new NavModel
      {
        Key = $"{servicePath}.select.nav",
        Elements = [new GridNavSearchModel { Key = "search", Label = grid.L["Search"] }]
      },
      ActionNav = new NavModel { Key = $"{servicePath}.select.actions" }
    };

    var selectLayout = PanelLayout.Row(servicePath)
      .Panel("main", selectGridModel)
      .Build();

    return new ScreenDto { Layout = selectLayout };
  }

  public static void LocalizeAndConfigureColumns<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IEnumerable<GridColumnDto> columns)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var gridType = grid.GetType();
    var dbModel = grid.DbContext.Model;
    foreach (var col in columns)
    {
      if (!string.IsNullOrEmpty(col.Label))
      {
        col.Label = ScreenBuilderHelpers.ResolveFieldLabel(grid.L, grid.Feature, col.Path, col.Label);
      }

      if (!string.IsNullOrEmpty(col.Title))
      {
        col.Title = grid.L[col.Title];
      }

      col.Options = ScreenBuilderHelpers.LocalizeOptions(grid.L, col.Options);
      if (!string.IsNullOrEmpty(col.ReferenceService))
      {
        var refServiceType = ModuleCatalog.Instance.FindServiceType(col.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, refServiceType, $"AsReference on '{col.Path}'");
      }

      if (col.Cell is GridCellPickerModel cellPicker && !string.IsNullOrEmpty(cellPicker.ReferenceService))
      {
        var pickerServiceType = col.PickerSourceType ?? ModuleCatalog.Instance.FindServiceType(cellPicker.ReferenceService);
        ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, pickerServiceType, $"PickedBy on '{col.Path}'");
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
        col.IsRequired = ColumnRequiredResolver.Resolve(typeof(TEntity), col.Path, dbModel)
          ?? (typeof(TRow) != typeof(TEntity) ? ColumnRequiredResolver.Resolve(typeof(TRow), col.Path, dbModel) ?? false : false);
      }

      if (col.IsFilterable && col.Operators == null)
      {
        var isRefOrSelect = col.ReferenceService != null || col.Options is { Count: > 0 } || col.Cell is GridCellReferenceModel;
        col.Operators = GridFilterOperators.ForField(col.Type, isRefOrSelect, col.TemporalType != null);
      }

      if (col.IsFilterable && col.Header == null)
      {
        if (col.Type is UiFieldType.Date or UiFieldType.DateTime)
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

  public static async Task<OverlayDto> ResolveFormOverlayForTypeAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    Type? formType,
    string formServicePath,
    CancellationToken ct = default)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (formType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(grid.GetType(), formType, "ResolveFormOverlay");
    }

    if (grid.State.CachedFormOverlays.TryGetValue(formServicePath, out var cachedOverlay))
    {
      return cachedOverlay;
    }

    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(formType, grid.DbContext, grid.PlatformApp) is IFormService formInstance)
        {
          var resolved = await formInstance.OverlayAsync(formServicePath, ct);
          grid.State.CachedFormOverlays[formServicePath] = resolved;
          return resolved;
        }
      }
      catch
      {
      }
    }

    var fallback = OverlayModel.Drawer($"{formServicePath}.modal").Screen(formServicePath).Build();
    grid.State.CachedFormOverlays[formServicePath] = fallback;
    return fallback;
  }

  private static async Task<GridNavStatusModel?> BuildStatusNavElementAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (grid.PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(grid.PlatformApp);
    }

    var statusPath = StatusMoveNavBuilder.ResolveStatusPath(statusFlow.StatusMember);
    var statusMoves = new List<GridNavStatusMoveDto>();

    foreach (var t in statusFlow.Transitions)
    {
      if (!StatusMoveNavBuilder.CanMove(grid.Feature, grid.User, t.Name))
      {
        continue;
      }

      var meta = await StatusMoveNavBuilder.BuildTransitionMetaAsync(grid.GetType(), grid.Feature, grid.DbContext, grid.PlatformApp, grid.User, grid.L, statusFlow, t, ct);
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
      Label = grid.L["Status"],
      EmptyTitle = grid.L["StatusNoMove"],
      Path = statusPath,
      Moves = statusMoves
    };
  }
}
