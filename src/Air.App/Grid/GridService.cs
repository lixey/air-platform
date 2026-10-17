using System.Linq.Expressions;
using Air.App.Components;
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
  Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
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

  public sealed class Nav
  {
    internal List<GridNavActionModel> Actions { get; } = [];
    internal List<GridNavPickerModel> Pickers { get; } = [];

    public Nav AddAction(
      Delegate method,
      Action<ButtonBuilder>? look = null,
      string? confirm = null,
      bool needsSelection = true)
    {
      var b = new ButtonBuilder();
      look?.Invoke(b);
      var action = new GridNavActionModel
      {
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = b.LabelText ?? method.Method.Name,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        NeedsSelection = needsSelection,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = confirm, Color = UiColor.Red } : null
      };
      Actions.Add(action);
      return this;
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

  public sealed class DeleteContext
  {
    public required IReadOnlyList<TEntity> Records { get; init; }
  }

  public sealed class Hooks
  {
    internal List<Func<DeleteContext, CancellationToken, Task>> BeforeDeleteActions { get; } = [];

    public void BeforeDelete(Func<DeleteContext, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<DeleteContext> handler) =>
      BeforeDeleteActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });
  }

  protected virtual void SetHooks(Hooks hooks) { }

  public sealed class GridBatchContext<TEntityRec>
  {
    public required IReadOnlyList<TEntityRec> Records { get; init; }
  }

  protected async Task<GridActionDto> ExecuteBatchAsync(
    GridRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default)
  {
    var ids = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : new List<long>());
    var records = await db.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
    var ctx = new GridBatchContext<TEntity> { Records = records };
    await handler(ctx, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  protected virtual void SetColumns(Columns<TRow> columns) { }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default)
  {
    var columns = new Columns<TRow>();
    SetColumns(columns);

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

    var nav = new Nav();
    SetNav(nav);

    var navStart = new List<NavElementModel>();
    if (formOverlay is not null && !Feature.IsReadOnly)
    {
      navStart.Add(new GridNavAddModel
      {
        Key = "add",
        Label = "Add",
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
      new GridNavSearchModel { Key = "search", Label = "Search" }
    };

    var actionEnd = new List<NavElementModel>();
    foreach (var act in nav.Actions)
    {
      actionEnd.Add(act);
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
        Label = "Delete",
        Confirm = new OverlayConfirmModel { Message = "Delete selected record(s)?", Color = UiColor.Red }
      });
    }

    var gridModel = new GridModel
    {
      Service = servicePath,
      Columns = columns.List.ToList(),
      IsReadOnly = Feature.IsReadOnly,
      Form = formOverlay,
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
        Elements = [],
        End = actionEnd
      }
    };

    var layout = PanelLayout.Row(servicePath)
      .Panel("main", gridModel)
      .Build();

    return Task.FromResult(new ScreenDto { Layout = layout });
  }

  public virtual async Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var parent = new Parent();
    SetParent(parent);

    var gridReq = request as GridRequest;
    ParentId = gridReq?.ParentId;

    var queryDeclaration = new Query();
    SetQuery(queryDeclaration);

    var query = BaseQuery(db);
    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

    if (ParentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, ParentId.Value);
    }

    if (queryDeclaration.Selector != null)
    {
      var projected = query.Select(queryDeclaration.Selector);
      var queryMap = QueryMap.FromEntity<TRow>().Build();
      var dataProvider = DataProvider.Create(projected, queryMap);
      var page = await dataProvider.GetPageAsync(request, ct);
      return new Page<object>
      {
        Rows = page.Rows.Cast<object>().ToList(),
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultQueryMap = QueryMap.FromEntity<TEntity>().Build();
    var defaultDataProvider = DataProvider.Create(query, defaultQueryMap);
    var defaultPage = await defaultDataProvider.GetPageAsync(request, ct);

    return new Page<object>
    {
      Rows = defaultPage.Rows.Cast<object>().ToList(),
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

    var hooks = new Hooks();
    SetHooks(hooks);

    var ctx = new DeleteContext { Records = [entity] };
    foreach (var h in hooks.BeforeDeleteActions)
    {
      await h(ctx, ct);
    }

    db.Set<TEntity>().Remove(entity);
    await db.SaveChangesAsync(ct);
  }
}

public abstract class GridService<TEntity>(PlatformDbContext db) : GridService<TEntity, TEntity>(db)
  where TEntity : class, IEntity, new();
