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
    var parent = new Parent();
    SetParent(parent);

    var columns = CreateColumns();

    var isSelect = request?.ReferenceLabelPaths is { Count: > 0 } || (request?.IsReferencePicker ?? false);
    if (isSelect)
    {
      return BuildSelectScreen(servicePath, columns, request);
    }

    var formOverlay = ResolveFormOverlay(columns);

    var nav = new Nav(servicePath, L);
    SetNav(nav);

    LocalizeAndConfigureColumns(columns.List);

    var isUserReadOnly = User != null && User.IsAuthenticated && !User.Can(Feature.Edit) && !User.Can(Feature.All);
    var isGridReadOnly = IsReadOnly || isUserReadOnly;

    var navStart = new List<NavElementModel>();
    if (!isGridReadOnly && nav.Creates.Count > 0)
    {
      foreach (var (createFormType, createModel) in nav.Creates)
      {
        createModel.Overlay = ResolveFormOverlayForType(createFormType, createModel.Service!);
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
      var methodInfo = entry.Method.Method;
      var reqAttr = methodInfo.GetCustomAttribute<RequireRightAttribute>(inherit: true);
      var reqAction = methodInfo.GetCustomAttribute<RequireActionAttribute>(inherit: true);
      var hasRight = true;
      if (reqAttr != null && User != null && User.IsAuthenticated)
      {
        if (Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat))
        {
          hasRight = User.Can(targetFeat.Action(reqAttr.ActionName)) || User.Can(targetFeat.All) || User.Can(Feature.All);
        }
      }
      else if (reqAction != null && User != null && User.IsAuthenticated)
      {
        hasRight = User.Can(Feature.Action(reqAction.ActionName)) || User.Can(Feature.All);
      }
      else if (!entry.IsAllowedInReadOnly && isUserReadOnly)
      {
        hasRight = false;
      }

      if (hasRight)
      {
        if (entry.Ask != null)
        {
          entry.Model.Ask = new FormAskModel
          {
            Title = entry.Model.Label,
            Message = entry.Model.Confirm?.Message,
            Color = entry.Model.Color,
            HasDefaults = entry.DefaultProvider != null,
            Elements = entry.Ask.BuildElements().Select(e =>
            {
              if (!string.IsNullOrEmpty(e.Label))
              {
                e.Label = L[e.Label];
              }

              return e;
            }).ToList()
          };
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
    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      var chosenKey = request?.NamedFilters?.FirstOrDefault(k =>
          group.Items.Any(i => string.Equals(i.Key, k, StringComparison.OrdinalIgnoreCase)))
        ?? group.Items.FirstOrDefault(i => i.IsDefault)?.Key;
      var groupItems = new List<GridNavNamedFilterModel>();
      foreach (var item in group.Items)
      {
        string? badgeVal = null;
        if (item.HasBadge)
        {
          try
          {
            var baseQ = RestrictedQuery();
            foreach (var filter in queryDeclaration.Filters)
            {
              baseQ = baseQ.Where(filter);
            }

            if (ParentId.HasValue && parent.FilterFunc != null)
            {
              baseQ = parent.FilterFunc(baseQ, ParentId.Value);
            }

            if (item.Predicate != null)
            {
              baseQ = baseQ.Where(item.Predicate);
            }

            badgeVal = (await baseQ.CountAsync(ct)).ToString(CultureInfo.InvariantCulture);
          }
          catch { }
        }

        groupItems.Add(new GridNavNamedFilterModel
        {
          Key = item.Key,
          Label = L[item.Label],
          Title = item.Title != null ? L[item.Title] : null,
          Color = item.Color,
          Badge = badgeVal,
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
      string? badgeValue = null;
      if (item.HasBadge)
      {
        try
        {
          var baseQ = RestrictedQuery();
          foreach (var filter in queryDeclaration.Filters)
          {
            baseQ = baseQ.Where(filter);
          }

          if (ParentId.HasValue && parent.FilterFunc != null)
          {
            baseQ = parent.FilterFunc(baseQ, ParentId.Value);
          }

          if (item.Predicate != null)
          {
            baseQ = baseQ.Where(item.Predicate);
          }

          badgeValue = (await baseQ.CountAsync(ct)).ToString(CultureInfo.InvariantCulture);
        }
        catch { }
      }

      navElements.Add(new GridNavNamedFilterModel
      {
        Key = item.Key,
        Label = L[item.Label],
        Title = item.Title != null ? L[item.Title] : null,
        Color = item.Color,
        Badge = badgeValue,
        IsRemembered = item.IsRemembered,
        IsSelected = request?.NamedFilters?.Any(k => string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)) == true
      });
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

    var inMenuActions = permittedActions.Where(a => a.Model.NeedsSelection && a.IsInMenu).Select(a => (NavElementModel)a.Model).ToList();
    if (inMenuActions.Count > 0)
    {
      actionElements.Add(new NavMenuModel
      {
        Key = "actions",
        Label = L["Actions"],
        Variant = ButtonVariant.Soft,
        Elements = inMenuActions
      });
    }

    foreach (var entry in permittedActions.Where(a => a.Model.NeedsSelection && !a.IsInMenu))
    {
      actionElements.Add(entry.Model);
    }

    var canExport = User == null || !User.IsAuthenticated || User.Can(Feature.Export) || User.Can(Feature.All);
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
    if (CanDelete())
    {
      actionEnd.Add(new GridNavActionModel
      {
        Key = "delete",
        Service = servicePath,
        Method = "delete",
        NeedsSelection = true,
        Color = UiColor.Red,
        Variant = ButtonVariant.Soft,
        Icon = UiIcon.Trash,
        Label = L["Delete"],
        Confirm = new OverlayConfirmModel { Message = L["DeleteConfirm"], Color = UiColor.Red }
      });
    }

    foreach (var rule in queryDeclaration.InitialFilters)
    {
      if (rule.Picked is null)
      {
        var col = columns.List.FirstOrDefault(c =>
          string.Equals(c.Path, rule.Field, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(c.FilterPath, rule.Field, StringComparison.OrdinalIgnoreCase));
        if (col?.Options is { Count: > 0 } opts)
        {
          if (rule.Value is System.Collections.IEnumerable enumerable and not string)
          {
            rule.Picked = enumerable.Cast<object?>()
              .Where(v => v != null)
              .Select(v =>
              {
                var id = Convert.ToInt32(v, CultureInfo.InvariantCulture);
                var opt = opts.FirstOrDefault(o => o.Id == id);
                return new PickedValue { Id = id, Label = opt?.Name ?? id.ToString(CultureInfo.InvariantCulture) };
              })
              .ToList();
          }
          else if (rule.Value is not null)
          {
            var id = Convert.ToInt32(rule.Value, CultureInfo.InvariantCulture);
            var opt = opts.FirstOrDefault(o => o.Id == id);
            rule.Picked = [new PickedValue { Id = id, Label = opt?.Name ?? id.ToString(CultureInfo.InvariantCulture) }];
          }
        }
      }
    }

    var initialFilter = queryDeclaration.InitialFilters.Count == 1
      ? (FilterNode)queryDeclaration.InitialFilters[0]
      : queryDeclaration.InitialFilters.Count > 1
        ? FilterNode.All([.. queryDeclaration.InitialFilters.Cast<FilterNode>()])
        : null;

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = columns.List.ToList(),
      PageSize = queryDeclaration.CustomPageSize ?? 200,
      IsReadOnly = isGridReadOnly,
      Form = formOverlay,
      InitialFilter = initialFilter,
      MutationTexts = new MutationTextsDto
      {
        ConflictTitle = L["MutationConflict"],
        ReloadLabel = L["MutationReload"],
        UnknownTitle = L["MutationUnknown"],
        UnknownDescription = L["MutationUnknownDescription"],
        VerifyLabel = L["MutationVerify"],
        CommittedWarningTitle = L["MutationCommittedWarning"],
        SelectionCorrectionLabel = L["MutationSelectionCorrection"]
      },
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
        col.Label = !string.IsNullOrEmpty(col.Path)
          ? L.TextOr($"{Feature.CleanName}.{col.Path}",
              L.TextOr($"{Feature.Label}.{col.Path}",
                L.TextOr($"{Feature.Name}.{col.Path}", L[col.Label])))
          : L[col.Label];
      }

      if (!string.IsNullOrEmpty(col.Title))
      {
        col.Title = L[col.Title];
      }

      if (col.Options is { Count: > 0 })
      {
        col.Options = col.Options.Select(opt => new OptionDto
        {
          Id = opt.Id,
          Name = L[opt.Name],
          Color = opt.Color,
          Title = opt.Title != null ? L[opt.Title] : null
        }).ToList();
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

  protected virtual OverlayDto? ResolveFormOverlay(Columns columns)
  {
    var detectedFormService = ResolveFormServicePath(columns);
    if (detectedFormService is null)
    {
      return null;
    }

    var formType = ResolveFormServiceType(columns) ?? ModuleCatalog.Instance.FindServiceType(detectedFormService);
    return ResolveFormOverlayForType(formType, detectedFormService);
  }

  private OverlayDto ResolveFormOverlayForType(Type? formType, string formServicePath)
  {
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService formInstance)
        {
          return formInstance.Overlay(formServicePath);
        }
      }
      catch
      {
        // Fallback to default dialog overlay below
      }
    }

    return OverlayModel.Dialog($"{formServicePath}.modal").Screen(formServicePath).Build();
  }

  private GridNavStatusModel? BuildStatusNavElement(string servicePath, IRecordStatuses<TEntity> statusFlow)
  {
    string? statusMemberName = null;
    if (statusFlow.StatusMember.Body is MemberExpression memberExpr)
    {
      statusMemberName = memberExpr.Member.Name;
    }
    else if (statusFlow.StatusMember.Body is UnaryExpression { Operand: MemberExpression ueMember })
    {
      statusMemberName = ueMember.Member.Name;
    }

    var statusPath = !string.IsNullOrEmpty(statusMemberName)
      ? char.ToLowerInvariant(statusMemberName[0]) + statusMemberName[1..]
      : "docStatusId";

    var statusMoves = statusFlow.Transitions
      .Where(t => User == null || !User.IsAuthenticated || User.Can(Feature.Action(t.Name)) || User.Can(Feature.Edit) || User.Can(Feature.All))
      .Select(t =>
      {
        var target = statusFlow.Statuses.FirstOrDefault(s => s.Id == t.To);
        var verbLabel = !string.IsNullOrEmpty(Feature.Module)
          ? L.TextOr($"{Feature.Module}: {t.Name}", L[t.Name])
          : L[t.Name];
        var targetLabel = target != null ? L[target.Name] : verbLabel;
        var confirmMessage = t.Confirm != null ? L[t.Confirm] : null;
        return new GridNavStatusMoveDto
        {
          From = [.. t.From],
          Action = new GridNavActionModel
          {
            Key = $"status.{t.Name.ToLowerInvariant()}",
            Label = verbLabel != t.Name ? verbLabel : targetLabel,
            Title = target?.Title != null ? L[target.Title] : null,
            Color = target?.Color,
            Service = servicePath,
            Method = $"move-status/{t.Name.ToLowerInvariant()}",
            NeedsSelection = true,
            Confirm = t.Ask is not null || confirmMessage is null
              ? null
              : new OverlayConfirmModel { Message = confirmMessage, Color = target?.Color },
            Ask = t.Ask is null
              ? null
              : new FormAskModel
              {
                Title = verbLabel != t.Name ? verbLabel : targetLabel,
                Message = confirmMessage,
                Color = target?.Color,
                Elements = [.. t.Ask.BuildElements().Select(e =>
                {
                  if (!string.IsNullOrEmpty(e.Label))
                  {
                    e.Label = L[e.Label];
                  }

                  return e;
                })]
              },
            UnavailableReasons = t.Offers.Count == 0
              ? null
              : [.. t.Offers.Select(o => L[o.Reason])]
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
