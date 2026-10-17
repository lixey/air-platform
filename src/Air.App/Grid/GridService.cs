using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Air.App.Components;
using Air.App.Grid.Selection;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Contracts;
using Air.Data.Entities;
using Air.Data.Querying;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Grid;

public interface IGridService
{
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) => BuildScreenAsync(servicePath, ct);
  Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default);
  Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, System.Text.Json.JsonElement? body, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default);
}

public abstract class GridService<TEntity, TRow>(PlatformDbContext db) : IGridService
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected Localizer L => Localizer.Instance;

  public virtual Feature Feature => new("", GetType().Name.Replace("GridService", "").ToLowerInvariant(), GetType());

  protected virtual bool IsReadOnly => Feature.IsReadOnly;
  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  public sealed class Parent
  {
    internal Func<IQueryable<TEntity>, long, IQueryable<TEntity>>? FilterFunc { get; private set; }
    public string? BoundParentService { get; private set; }

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      var param = member.Parameters[0];
      FilterFunc = (q, parentId) =>
      {
        var body = Expression.Equal(member.Body, Expression.Constant(parentId));
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
        return q.Where(lambda);
      };
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
    {
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      var param = member.Parameters[0];
      FilterFunc = (q, parentId) =>
      {
        var body = Expression.Equal(member.Body, Expression.Constant((long?)parentId, typeof(long?)));
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
        return q.Where(lambda);
      };
    }

    public void Read<TParentService>()
    {
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    }
  }

  protected virtual void SetParent(Parent parent) { }
  protected internal long? ParentId { get; set; }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity
  {
    if (ParentId == null || ParentId.Value <= 0)
      return null;
    var tracked = db.Set<TParent>().Local.FirstOrDefault(e => e.Id == ParentId.Value);
    if (tracked != null) return tracked;
    var efModel = db.Model.FindEntityType(typeof(TParent));
    IQueryable<TParent> query = db.Set<TParent>();
    if (efModel != null)
    {
      foreach (var nav in efModel.GetNavigations())
      {
        if (!nav.IsCollection)
          query = query.Include(nav.Name);
      }
    }
    return query.FirstOrDefault(e => e.Id == ParentId.Value);
  }

  public sealed class Query
  {
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];
    internal LambdaExpression? LabelSelector { get; private set; }

    public Query RecordLabel<TProp>(Expression<Func<TEntity, TProp>> expr)
    {
      LabelSelector = expr;
      return this;
    }

    public Query Where(Expression<Func<TEntity, bool>> predicate)
    {
      Filters.Add(predicate);
      return this;
    }

    public Expression<Func<TEntity, TRow>>? Selector { get; private set; }

    public Query Select(Expression<Func<TEntity, TRow>> selector)
    {
      Selector = selector;
      return this;
    }

    public Query SortBy<TProp>(Expression<Func<TRow, TProp>> expr) => this;
    public Query SortByDescending<TProp>(Expression<Func<TRow, TProp>> expr) => this;
    public Query ThenBy<TProp>(Expression<Func<TRow, TProp>> expr) => this;
    public Query SearchBy(params Expression<Func<TRow, object?>>[] exprs) => this;
    public InitialFilterBuilder<TProp> InitialFilter<TProp>(Expression<Func<TRow, TProp>> expr) => new(this);

    public sealed class InitialFilterBuilder<TProp>(Query query)
    {
      public Query In(params TProp[] values) => query;
    }
  }

  protected virtual void SetQuery(Query query) { }

  public sealed class ButtonBuilder
  {
    public string? LabelText { get; private set; }
    public string? IconHandle { get; private set; }
    public string? ColorHandle { get; private set; }
    public string? VariantHandle { get; private set; }
    public bool IsInMenu { get; private set; }

    public ButtonBuilder Label(string label) { LabelText = label; return this; }
    public ButtonBuilder Icon(string icon) { IconHandle = icon; return this; }
    public ButtonBuilder Color(string color) { ColorHandle = color; return this; }
    public ButtonBuilder Variant(string variant) { VariantHandle = variant; return this; }
    public ButtonBuilder Soft() { VariantHandle = ButtonVariant.Soft; return this; }
    public ButtonBuilder InMenu() { IsInMenu = true; return this; }
  }

  public sealed class NavPickerBuilder(GridNavPickerModel model)
  {
    public NavPickerBuilder MinLength(int len) { model.MinLength = len; return this; }
    public NavPickerBuilder ReadsParent() { model.ReadsParent = true; return this; }
  }

  public sealed class NavActionBuilder
  {
    private readonly (GridNavActionModel Model, bool IsInMenu) _item;
    private readonly Nav _nav;

    internal NavActionBuilder((GridNavActionModel Model, bool IsInMenu) item, Nav nav)
    {
      _item = item;
      _nav = nav;
    }

    public Nav InMenu()
    {
      var idx = _nav.Actions.IndexOf(_item);
      if (idx >= 0)
      {
        _nav.Actions[idx] = (_item.Model, true);
      }
      return _nav;
    }
  }

  public sealed class Nav(string servicePath = "")
  {
    internal List<(GridNavActionModel Model, bool IsInMenu)> Actions { get; } = [];
    internal List<GridNavPickerModel> Pickers { get; } = [];

    public NavActionBuilder AddAction(
      Delegate method,
      Action<ButtonBuilder>? look = null,
      string? confirm = null,
      bool needsSelection = true)
    {
      var b = new ButtonBuilder();
      look?.Invoke(b);
      var action = new GridNavActionModel
      {
        Service = servicePath,
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = b.LabelText ?? method.Method.Name,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        NeedsSelection = needsSelection,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = confirm, Color = UiColor.Red } : null
      };
      var item = (action, b.IsInMenu);
      Actions.Add(item);
      return new NavActionBuilder(item, this);
    }

    public NavPickerBuilder AddPicker<TSource>(
      Delegate method,
      string? label = null)
    {
      var picker = new GridNavPickerModel
      {
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = label,
        ReferenceService = ServicePathResolver.Resolve(typeof(TSource)),
        MinLength = 2
      };
      Pickers.Add(picker);
      return new NavPickerBuilder(picker);
    }
  }

  protected virtual void SetNav(Nav nav) { }

  public sealed class Hooks
  {
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

    public void BeforeDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<GridDeleteContext<TEntity>> handler) =>
      BeforeDeleteActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });
    public void ValidateDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDelete(handler);
    public void ValidateDelete(Action<GridDeleteContext<TEntity>> handler) => BeforeDelete(handler);

    public void AfterDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);
    public void AfterDelete(Action<GridDeleteContext<TEntity>> handler) =>
      AfterDeleteActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });
  }

  protected virtual void SetHooks(Hooks hooks) { }

  protected async Task<GridActionDto> ExecuteBatchAsync(
    GridRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default)
  {
    var ids = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : new List<long>());
    var records = await db.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
    var ctx = new GridBatchContext<TEntity>(ids, records, ct => db.SaveChangesAsync(ct));
    await handler(ctx, cancellationToken);
    if (ctx.Errors.Count > 0)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = ctx.Errors.Select(e => e.ToMessage("error")).ToList()
      };
    }
    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto
    {
      Outcome = MutationOutcome.Committed,
      OpenScreen = ctx.OpenScreen,
      OpenId = ctx.OpenId
    };
  }

  protected virtual void SetColumns(Columns<TRow> columns) { }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default)
  {
    var columns = new Columns<TRow>();
    SetColumns(columns);

    var isSelect = request?.ReferenceLabelPaths is { Count: > 0 } || (request?.IsReferencePicker ?? false);
    if (isSelect)
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

      foreach (var col in matching)
      {
        if (!string.IsNullOrEmpty(col.Label))
        {
          col.Label = Localizer.Instance[col.Label];
        }
      }

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

      return Task.FromResult(new ScreenDto { Layout = selectLayout });
    }

    string? detectedFormService = FormServicePath;
    if (detectedFormService == null && columns.FormServiceType != null)
    {
      detectedFormService = ServicePathResolver.Resolve(columns.FormServiceType);
    }
    else if (detectedFormService == null)
    {
      var candidateFormName = GetType().Name.Replace("GridService", "FormService");
      var candidateType = GetType().Assembly.GetType(GetType().Namespace + "." + candidateFormName)
        ?? GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateFormName);
      if (candidateType != null)
      {
        detectedFormService = ServicePathResolver.Resolve(candidateType);
      }
    }

    OverlayModelDto? formOverlay = null;
    if (detectedFormService is not null)
    {
      var formType = ModuleCatalog.Instance.FindServiceType(detectedFormService);
      if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
      {
        try
        {
          var formInstance = (IFormService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(formType);
          formOverlay = formInstance.Overlay(detectedFormService);
        }
        catch
        {
          formOverlay = OverlayModel.Dialog($"{detectedFormService}.modal").Screen(detectedFormService).Build();
        }
      }
      else
      {
        formOverlay = OverlayModel.Dialog($"{detectedFormService}.modal").Screen(detectedFormService).Build();
      }
    }

    var nav = new Nav(servicePath);
    SetNav(nav);

    foreach (var col in columns.List)
    {
      if (!string.IsNullOrEmpty(col.Label))
      {
        col.Label = Localizer.Instance[col.Label];
      }
    }

    var navStart = new List<NavElementModel>();
    if (formOverlay is not null && !Feature.IsReadOnly)
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

    foreach (var picker in nav.Pickers)
    {
      navStart.Add(picker);
    }

    var navElements = new List<NavElementModel>
    {
      new GridNavSearchModel { Key = "search", Label = Localizer.Instance["Search"] }
    };

    var actionEnd = new List<NavElementModel>();
    var actionMore = new List<NavElementModel>();
    foreach (var (act, inMenu) in nav.Actions)
    {
      if (inMenu)
      {
        actionMore.Add(act);
      }
      else
      {
        actionEnd.Add(act);
      }
    }

    if (!Feature.IsReadOnly)
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
    if (columns.StatusFlow is Screens.IRecordStatuses<TEntity> statusFlow)
    {
      var statusMoves = statusFlow.Transitions.Select(t =>
      {
        var target = statusFlow.Statuses.FirstOrDefault(s => s.Id == t.To);
        return new GridNavStatusMoveDto
        {
          From = [.. t.From],
          Action = new GridNavActionModel
          {
            Key = $"status.{t.Name.ToLowerInvariant()}",
            Label = target?.Name ?? t.Name,
            Color = target?.Color,
            Service = servicePath,
            Method = $"move-status/{t.Name.ToLowerInvariant()}",
            NeedsSelection = true,
            Confirm = t.Ask is not null || t.Confirm is null
              ? null
              : new OverlayConfirmModel { Message = t.Confirm, Color = target?.Color },
            Ask = t.Ask is null
              ? null
              : new FormAskModel
              {
                Title = target?.Name ?? t.Name,
                Message = t.Confirm,
                Color = target?.Color,
                Elements = [.. t.Ask.BuildElements()]
              }
          }
        };
      }).ToList();

      var statusButton = new GridNavStatusModel
      {
        Key = "status",
        Label = Localizer.Instance["Status"],
        Path = "docStatusId",
        Moves = statusMoves
      };
      actionElements.Add(statusButton);
    }

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = columns.List.ToList(),
      IsReadOnly = Feature.IsReadOnly,
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
        End = []
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

  public virtual async Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, System.Text.Json.JsonElement? body, CancellationToken ct = default)
  {
    var cols = new Columns<TRow>();
    SetColumns(cols);
    if (cols.StatusFlow is not Screens.IRecordStatuses<TEntity> statusFlow)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No status flow configured for this grid.", Type = MessageType.Error }] };
    }

    var transition = statusFlow.Transitions.FirstOrDefault(t => string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));
    if (transition is null)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = $"Unknown status transition '{transitionName}'.", Type = MessageType.Error }] };
    }

    var selectedIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    if (selectedIds.Count == 0)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No records selected.", Type = MessageType.Error }] };
    }

    var entities = await db.Set<TEntity>().Where(e => selectedIds.Contains(e.Id)).ToListAsync(ct);
    var targetStatus = statusFlow.Statuses.FirstOrDefault(s => s.Id == transition.To);
    var statusProp = typeof(TEntity).GetProperty("DocStatusId") ?? typeof(TEntity).GetProperty("StatusId");

    foreach (var entity in entities)
    {
      var currentStatus = statusFlow.StatusMember.Compile()(entity);
      if (!transition.From.Contains(currentStatus)) continue;

      if (statusProp != null && targetStatus != null)
      {
        statusProp.SetValue(entity, targetStatus.Id);
      }
    }

    await db.SaveChangesAsync(ct);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public virtual async Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var parent = new Parent();
    SetParent(parent);

    var gridReq = request as GridRequest;
    ParentId = gridReq?.ParentId;

    var queryDeclaration = new Query();
    SetQuery(queryDeclaration);

    var cols = new Columns<TRow>();
    SetColumns(cols);

    var query = BaseQuery(db);
    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

    if (ParentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, ParentId.Value);
    }

    // Auto-include reference navigation properties declared in columns
    var entityType = typeof(TEntity);
    foreach (var col in cols.List)
    {
      if (col.ReferenceService != null && !string.IsNullOrEmpty(col.Path))
      {
        var prop = entityType.GetProperty(col.Path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null && !prop.PropertyType.IsValueType && prop.PropertyType != typeof(string))
        {
          try
          {
            query = query.Include(prop.Name);
          }
          catch { }
        }
      }
    }

    if (queryDeclaration.Selector != null)
    {
      var projected = query.Select(queryDeclaration.Selector);
      var queryMap = QueryMap.FromEntity<TRow>().Build();
      var dataProvider = DataProvider.Create(projected, queryMap);
      var page = await dataProvider.GetPageAsync(request, ct);
      var shapedRows = page.Rows.Select(r => ShapeRow(r, cols.List)).ToList();
      return new Page<object>
      {
        Rows = shapedRows,
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultQueryMap = QueryMap.FromEntity<TEntity>().Build();
    var defaultDataProvider = DataProvider.Create(query, defaultQueryMap);
    var defaultPage = await defaultDataProvider.GetPageAsync(request, ct);
    var defaultShapedRows = defaultPage.Rows.Select(r => ShapeRow((r as TRow)!, cols.List)).ToList();

    return new Page<object>
    {
      Rows = defaultShapedRows,
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  protected virtual object ShapeRow(TRow row, IReadOnlyList<GridColumnDto> columns)
  {
    if (row is null || columns.Count == 0) return row!;

    var shaped = new Dictionary<string, object?>(StringComparer.Ordinal);
    var idProp = typeof(TRow).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    if (idProp is not null && idProp.GetValue(row) is { } idVal)
    {
      shaped["id"] = idVal;
    }
    var verProp = typeof(TRow).GetProperty("Ver", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    if (verProp is not null && verProp.GetValue(row) is { } verVal)
    {
      shaped["ver"] = verVal;
    }

    foreach (var column in columns)
    {
      if (string.IsNullOrEmpty(column.Path)) continue;

      var value = ReadPathValue(row, column.Path);

      if (column.Options is { Count: > 0 } options)
      {
        if (value is not null)
        {
          var id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
          var name = options.FirstOrDefault(o => o.Id == id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);
          value = new Reference { Id = id, Name = name };
        }
      }
      else if (column.ReferenceService is not null && value is not null and not Reference)
      {
        if (value is IEntity refEntity)
        {
          var nameProp = value.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
          var name = nameProp?.GetValue(value)?.ToString() ?? refEntity.Id.ToString(CultureInfo.InvariantCulture);
          value = new Reference { Id = refEntity.Id, Name = name };
        }
      }

      PutValue(shaped, column.Path.Split('.'), value);
    }

    return shaped;
  }

  private static object? ReadPathValue(object? target, string path)
  {
    if (target is null) return null;
    var current = target;
    foreach (var segment in path.Split('.'))
    {
      if (current is null) return null;
      if (current is IDictionary<string, object?> dict)
      {
        dict.TryGetValue(segment, out current);
        continue;
      }
      var prop = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
      if (prop is null) return null;
      current = prop.GetValue(current);
    }
    return current;
  }

  private static void PutValue(Dictionary<string, object?> target, string[] segments, object? value)
  {
    var current = target;
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var key = ToCamel(segments[i]);
      if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> nested)
      {
        nested = new Dictionary<string, object?>(StringComparer.Ordinal);
        current[key] = nested;
      }
      current = nested;
    }
    current[ToCamel(segments[^1])] = value;
  }

  private static string ToCamel(string str) =>
    string.IsNullOrEmpty(str) ? str : char.ToLowerInvariant(str[0]) + str[1..];


  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentId = request.ParentId;
    var ids = request.SelectedIds ?? [];
    if (ids.Count == 0 && request.Id.HasValue)
    {
      ids = [request.Id.Value];
    }
    if (ids.Count == 0)
    {
      return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
    }

    var entities = await db.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
    var hooks = new Hooks();
    SetHooks(hooks);

    var ctx = new GridDeleteContext<TEntity>(ids, entities, token => db.SaveChangesAsync(token));
    foreach (var h in hooks.BeforeDeleteActions)
    {
      await h(ctx, ct);
    }

    if (ctx.Errors.Count > 0)
    {
      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Refused,
        Deleted = 0,
        Warnings = [.. ctx.Errors.Select(e => e.ToMessage())]
      };
    }

    db.Set<TEntity>().RemoveRange(entities);
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterDeleteActions)
    {
      await h(ctx, ct);
    }
    await db.SaveChangesAsync(ct);

    return new GridDeleteDto
    {
      Outcome = MutationOutcome.Committed,
      Deleted = entities.Count
    };
  }

  public virtual async Task<object?> ReadRowAsync(long id, CancellationToken ct = default)
  {
    var parent = new Parent();
    SetParent(parent);

    var queryDeclaration = new Query();
    SetQuery(queryDeclaration);

    var cols = new Columns<TRow>();
    SetColumns(cols);

    var query = BaseQuery(db).Where(e => e.Id == id);
    if (ParentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, ParentId.Value);
    }

    var entityType = typeof(TEntity);
    foreach (var col in cols.List)
    {
      if (col.ReferenceService != null && !string.IsNullOrEmpty(col.Path))
      {
        var prop = entityType.GetProperty(col.Path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null && !prop.PropertyType.IsValueType && prop.PropertyType != typeof(string))
        {
          try
          {
            query = query.Include(prop.Name);
          }
          catch { }
        }
      }
    }

    if (queryDeclaration.Selector != null)
    {
      var row = await query.Select(queryDeclaration.Selector).FirstOrDefaultAsync(ct);
      return row is null ? null : ShapeRow(row, cols.List);
    }

    var entity = await query.FirstOrDefaultAsync(ct);
    return entity is null ? null : ShapeRow((entity as TRow)!, cols.List);
  }

  public virtual async Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default)
  {
    ParentId = request.ParentId;
    if (request.Id is not > 0)
    {
      throw new KeyNotFoundException(Localizer.Instance["FormNotFound"]);
    }

    var cols = new Columns<TRow>();
    SetColumns(cols);

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase));
    if (col == null || !col.IsEditable)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    Type? formType = cols.FormServiceType;
    if (formType == null && FormServicePath != null)
    {
      formType = ModuleCatalog.Instance.FindServiceType(FormServicePath);
    }
    if (formType == null)
    {
      var candidateFormName = GetType().Name.Replace("GridService", "FormService");
      formType = GetType().Assembly.GetType(GetType().Namespace + "." + candidateFormName)
        ?? GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateFormName);
    }

    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      var form = (IFormService)services.GetRequiredService(formType);
      var formReq = new FormRequest
      {
        Id = request.Id,
        Ver = request.Ver,
        ParentId = request.ParentId,
        ParentService = request.ParentService
      };
      return await form.SaveCellAsync(request.Id.Value, request.Path, request.Value, formReq, token => ReadRowAsync(request.Id.Value, token), ct);
    }

    var entity = await db.Set<TEntity>().FindAsync([request.Id.Value], ct)
      ?? throw new KeyNotFoundException(Localizer.Instance["FormNotFound"]);

    FormService<TEntity, TEntity>.SetPropertyValue(entity, request.Path, request.Value);
    db.Set<TEntity>().Update(entity);
    await db.SaveChangesAsync(ct);

    var updatedRow = await ReadRowAsync(request.Id.Value, ct);
    return new GridCellDto
    {
      Row = updatedRow,
      Outcome = MutationOutcome.Committed
    };
  }
}

public abstract class GridService<TEntity>(PlatformDbContext db) : GridService<TEntity, TEntity>(db)
  where TEntity : class, IEntity, new();
