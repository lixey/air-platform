using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Air.App.Components;
using Air.App.Form;
using Air.App.Grid.Selection;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Mutations;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data.Contracts;
using Air.Data.Entities;

namespace Air.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default)
  {
    var columns = CreateColumns();

    var isSelect = request?.ReferenceLabelPaths is { Count: > 0 } || (request?.IsReferencePicker ?? false);
    if (isSelect)
    {
      return Task.FromResult(BuildSelectScreen(servicePath, columns, request));
    }

    var formOverlay = ResolveFormOverlay(columns);

    var nav = new Nav(servicePath);
    SetNav(nav);

    LocalizeAndConfigureColumns(columns.List);

    var isUserReadOnly = User != null && User.IsAuthenticated && !User.Can(Feature.Edit) && !User.Can(Feature.All);
    var isGridReadOnly = Feature.IsReadOnly || isUserReadOnly;

    var navStart = new List<NavElementModel>();
    if (formOverlay is not null && !isGridReadOnly)
    {
      navStart.Add(new GridNavAddModel
      {
        Key = "add",
        Label = Localizer.Instance["Add"],
        Variant = ButtonVariant.Soft,
        Color = UiColor.Orange,
        Icon = UiIcon.Plus
      });
    }

    navStart.AddRange(nav.Pickers);
    navStart.AddRange(nav.CustomStart);

    var navElements = new List<NavElementModel>
    {
      new GridNavSearchModel { Key = "search", Label = Localizer.Instance["Search"] }
    };

    var queryDeclaration = new Query();
    SetQuery(queryDeclaration);
    foreach (var item in queryDeclaration.NamedFilters)
    {
      string? badgeValue = null;
      if (item.HasBadge)
      {
        try
        {
          var baseQ = BaseQuery(db);
          foreach (var filter in queryDeclaration.Filters) baseQ = baseQ.Where(filter);
          badgeValue = baseQ.Where(item.Predicate).Count().ToString(CultureInfo.InvariantCulture);
        }
        catch { }
      }

      navElements.Add(new GridNavNamedFilterModel
      {
        Key = item.Key,
        Label = Localizer.Instance[item.Label],
        Badge = badgeValue,
        IsRemembered = item.IsRemembered,
        IsSelected = false
      });
    }

    var actionEnd = new List<NavElementModel>();
    var actionMore = new List<NavElementModel>();
    foreach (var entry in nav.Actions)
    {
      var methodInfo = entry.Method.Method;
      var reqAttr = methodInfo.GetCustomAttribute<Security.RequireRightAttribute>();
      var hasRight = true;
      if (reqAttr != null && User != null && User.IsAuthenticated)
      {
        if (Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat))
        {
          hasRight = User.Can(targetFeat.Action(reqAttr.Action.ToString())) || User.Can(Feature.All);
        }
      }
      else if (!entry.IsAllowedInReadOnly && isUserReadOnly)
      {
        hasRight = false;
      }

      if (!hasRight)
      {
        continue;
      }

      if (entry.IsInMenu)
      {
        actionMore.Add(entry.Model);
      }
      else
      {
        actionEnd.Add(entry.Model);
      }
    }

    actionEnd.Add(new GridNavActionModel
    {
      Key = "export",
      Service = servicePath,
      Method = "export",
      NeedsSelection = true,
      IsDownload = true,
      Color = UiColor.Green,
      Variant = ButtonVariant.Soft,
      Icon = UiIcon.Excel,
      Label = Localizer.Instance["Export"]
    });

    if (!isGridReadOnly && (User == null || !User.IsAuthenticated || User.Can(Feature.Delete) || User.Can(Feature.All)))
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
        Label = Localizer.Instance["Delete"],
        Confirm = new OverlayConfirmModel { Message = Localizer.Instance["DeleteConfirm"], Color = UiColor.Red }
      });
    }

    var actionElements = new List<NavElementModel>();
    if (columns.StatusFlow is IRecordStatuses<TEntity> statusFlow && !isGridReadOnly)
    {
      var statusButton = BuildStatusNavElement(servicePath, statusFlow);
      if (statusButton != null)
      {
        actionElements.Add(statusButton);
      }
    }

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = columns.List.ToList(),
      IsReadOnly = isGridReadOnly,
      Form = formOverlay,
      MutationTexts = new MutationTextsDto
      {
        ConflictTitle = Localizer.Instance["MutationConflict"],
        ReloadLabel = Localizer.Instance["MutationReload"],
        UnknownTitle = Localizer.Instance["MutationUnknown"],
        UnknownDescription = Localizer.Instance["MutationUnknownDescription"],
        VerifyLabel = Localizer.Instance["MutationVerify"],
        CommittedWarningTitle = Localizer.Instance["MutationCommittedWarning"],
        SelectionCorrectionLabel = Localizer.Instance["MutationSelectionCorrection"]
      },
      Nav = new NavModel
      {
        Key = $"{servicePath}.nav",
        Start = navStart,
        Elements = navElements,
        End = [],
        More = [
          new GridNavActionModel
          {
            Key = "export",
            Service = servicePath,
            Method = "export",
            NeedsSelection = false,
            IsDownload = true,
            Color = UiColor.Green,
            Icon = UiIcon.Excel,
            Label = Localizer.Instance["Export"]
          }
        ]
      },
      ActionNav = new NavModel
      {
        Key = $"{servicePath}.actions",
        Start = [],
        Elements = actionElements,
        End = actionEnd,
        More = actionMore
      }
    };

    var layout = PanelLayout.Row(servicePath)
      .Panel("main", gridModel)
      .Build();

    return Task.FromResult(new ScreenDto { Layout = layout });
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
      if (fallback != null) matching.Add(fallback);
    }

    LocalizeAndConfigureColumns(matching);

    var selectGridModel = new GridModel
    {
      Service = servicePath,
      Columns = matching,
      IsReadOnly = true,
      Form = new OverlayModelDto(),
      Nav = new NavModel { Key = $"{servicePath}.select.nav" },
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
          ? Localizer.Instance.TextOr($"{Feature.CleanName}.{col.Path}",
              Localizer.Instance.TextOr($"{Feature.Label}.{col.Path}",
                Localizer.Instance.TextOr($"{Feature.Name}.{col.Path}", Localizer.Instance[col.Label])))
          : Localizer.Instance[col.Label];
      }
      if (col.IsFilterable && col.Operators == null)
      {
        col.Operators = GridFilterOperators.ForField(col.Type, col.ReferenceService != null, col.TemporalType != null);
      }
    }
  }

  private OverlayModelDto? ResolveFormOverlay(Columns columns)
  {
    var detectedFormService = ResolveFormServicePath(columns);
    if (detectedFormService is null) return null;

    var formType = ResolveFormServiceType(columns) ?? ModuleCatalog.Instance.FindServiceType(detectedFormService);
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, _app) is IFormService formInstance)
        {
          return formInstance.Overlay(detectedFormService);
        }
      }
      catch
      {
        // Fallback to default dialog overlay below
      }
    }

    return OverlayModel.Dialog($"{detectedFormService}.modal").Screen(detectedFormService).Build();
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
        var verbLabel = Localizer.Instance.TextOr($"Ecom: {t.Name}", Localizer.Instance[t.Name]);
        var targetLabel = target != null ? Localizer.Instance[target.Name] : verbLabel;
        var confirmMessage = t.Confirm != null ? Localizer.Instance[t.Confirm] : null;
        return new GridNavStatusMoveDto
        {
          From = [.. t.From],
          Action = new GridNavActionModel
          {
            Key = $"status.{t.Name.ToLowerInvariant()}",
            Label = verbLabel != t.Name ? verbLabel : targetLabel,
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
                  if (!string.IsNullOrEmpty(e.Label)) e.Label = Localizer.Instance[e.Label];
                  return e;
                })]
              }
          }
        };
      }).ToList();

    if (statusMoves.Count == 0) return null;

    return new GridNavStatusModel
    {
      Key = "status",
      Label = Localizer.Instance["Status"],
      Path = statusPath,
      Moves = statusMoves
    };
  }
}
