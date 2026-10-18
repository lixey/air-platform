using System.Text.Json;
using System.Text.RegularExpressions;
using Air.App.Components;
using Air.App.Form;
using Air.App.Grid.Export;
using Air.App.Grid.Selection;
using Air.App.Internal;
using Air.App.Localization;
using Air.App.Mutations;
using Air.App.Screens;
using Air.Data.Contracts;
using Air.Data.Entities;
using Air.Data.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
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

  public virtual async Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default)
  {
    var cols = CreateColumns();
    if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No status flow configured for this grid.", Type = MessageType.Error }] };
    }

    var transition = statusFlow.Transitions.FirstOrDefault(t => string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));
    if (transition is null)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = $"Unknown status transition '{transitionName}'.", Type = MessageType.Error }] };
    }

    if (User != null && User.IsAuthenticated && !User.Can(Feature.Action(transition.Name)) && !User.Can(Feature.Edit) && !User.Can(Feature.All))
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }] };
    }

    var selectedIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    if (selectedIds.Count == 0)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No records selected.", Type = MessageType.Error }] };
    }

    var entities = await db.Set<TEntity>().Where(e => selectedIds.Contains(e.Id)).ToListAsync(ct);
    var context = new MutationContext(db);
    object? input = transition.Ask?.Read(body);

    await statusFlow.MoveAsync(context, entities, transition.Name, input, ct);

    if (context.Errors.Count > 0)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
      };
    }

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    if (User != null && User.IsAuthenticated && !User.Can(Feature.Delete) && !User.Can(Feature.All))
    {
      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Refused,
        Deleted = 0,
        Warnings = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
      };
    }

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

    var cols = CreateColumns();

    var query = BaseQuery(db).Where(e => e.Id == id);
    if (ParentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, ParentId.Value);
    }

    query = IncludeColumnNavigations(query, cols.List);

    if (queryDeclaration.Selector != null)
    {
      var row = await query.Select(queryDeclaration.Selector).FirstOrDefaultAsync(ct);
      if (row is null) return null;
      if (queryDeclaration.ComputeAction != null)
      {
        await queryDeclaration.ComputeAction([row], ct);
      }
      var shaped = ShapeRow(row, cols.List);
      await PopulateProjectedColorsAsync([shaped], cols.List, ct);
      return shaped;
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

    var cols = CreateColumns();

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase));
    if (col == null || !col.IsEditable)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var formType = ResolveFormServiceType(cols);
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      var form = (IFormService)services.GetRequiredService(formType);
      if (_app != null)
      {
        form.AttachPlatformApp(_app);
      }
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

  public virtual async Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default)
  {
    var query = BuildFilteredEntityQuery(request, out _, out var queryDeclaration);
    var cols = CreateColumns();
    var exportColumns = cols.List.Where(c => !c.IsHidden && !string.IsNullOrWhiteSpace(c.Path) && c.IsSortable).ToList();

    if (request.SelectedIds is { Count: > 0 } selectedIds)
    {
      query = query.Where(e => selectedIds.Contains(e.Id));
    }

    query = IncludeColumnNavigations(query, exportColumns);

    if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
    {
      request.Sort = queryDeclaration.DefaultSort.ToList();
    }

    if (request.SearchFields is not { Count: > 0 } && queryDeclaration.SearchFields is { Count: > 0 })
    {
      request.SearchFields = queryDeclaration.SearchFields.ToList();
    }

    const int limit = 100_000;
    IAsyncEnumerable<object> rowStream;

    if (queryDeclaration.Selector != null)
    {
      var queryMap = QueryMap.FromSelector(queryDeclaration.Selector).Build();
      var dataProvider = DataProvider.Create(query, queryMap, queryDeclaration.Selector);
      if (queryDeclaration.ComputeAction != null)
      {
        var page = await dataProvider.GetPageAsync(new ListRequest
        {
          Page = 1,
          PageSize = limit + 1,
          Filter = request.Filter,
          ColumnFilter = request.ColumnFilter,
          Sort = request.Sort,
          Search = request.Search,
          SearchFields = request.SearchFields
        }, ct);
        if (page.Rows.Count > 0)
        {
          await queryDeclaration.ComputeAction(page.Rows, ct);
        }
        rowStream = ToAsyncEnumerable(page.Rows.Select(r => ShapeRow(r, cols.List)));
      }
      else
      {
        rowStream = ShapeStreamAsync(dataProvider.StreamAsync(request, limit + 1, ct), cols.List, ct);
      }
    }
    else
    {
      var defaultQueryMap = BuildDefaultQueryMap(cols.List);
      var defaultDataProvider = DataProvider.Create(query, defaultQueryMap);
      rowStream = ShapeEntityStreamAsync(defaultDataProvider.StreamAsync(request, limit + 1, ct), cols.List, ct);
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    var label = L.TryGetString(Feature.Name, out var l) ? l : (string.IsNullOrEmpty(Feature.Name) ? typeof(TEntity).Name : Feature.Name);
    var cleanName = Regex.Replace(label, @"[\s\W]+", "_").Trim('_');
    var fileName = $"{cleanName}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";

    await GridExcelWriter.WriteAsync(
      fileStream,
      label,
      exportColumns,
      col => PropertyAccessorCache.GetProperty(typeof(TRow), col.Path ?? "")?.PropertyType,
      rowStream,
      L,
      TimeZoneInfo.Utc,
      limit,
      $"Export truncated to {limit} rows.",
      ct
    );

    fileStream.Position = 0;
    return Results.File(fileStream, GridExcelWriter.ContentType, fileName);
  }
}
