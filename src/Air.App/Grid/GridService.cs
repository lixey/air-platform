using Air.App.Modules;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Contracts;
using Air.Data.Entities;
using Air.Data.Querying;

namespace Air.App.Grid;

public interface IGridService
{
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
  Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
}

public abstract class GridService<TEntity, TRow>(PlatformDbContext db) : IGridService
  where TEntity : class, IEntity
  where TRow : class
{
  public virtual Feature Feature => new("", GetType().Name.Replace("GridService", "").ToLowerInvariant(), GetType());

  protected virtual string? FormServicePath => null;
  protected virtual OverlayModelDto? FormOverlay => FormServicePath is not null
    ? OverlayModel.Dialog(FormServicePath).Screen(FormServicePath).Width(720).Build()
    : null;

  protected virtual void SetColumns(Columns<TRow> columns) { }

  protected virtual IQueryable<TEntity> Query(PlatformDbContext context) => context.Set<TEntity>();

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default)
  {
    var columns = new Columns<TRow>();
    SetColumns(columns);

    var formOverlay = FormOverlay;
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

    var navElements = new List<NavElementModel>
    {
      new GridNavSearchModel { Key = "search", Label = "Search" }
    };

    var actionEnd = new List<NavElementModel>();
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
    var queryMap = QueryMap.FromEntity<TEntity>().Build();
    var dataProvider = DataProvider.Create(Query(db), queryMap);
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

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");
    db.Set<TEntity>().Remove(entity);
    await db.SaveChangesAsync(ct);
  }
}

public abstract class GridService<TEntity>(PlatformDbContext db) : GridService<TEntity, TEntity>(db)
  where TEntity : class, IEntity;
