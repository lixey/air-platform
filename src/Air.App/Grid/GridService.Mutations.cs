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
    ParentId = request.ParentId;
    var ids = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : new List<long>());
    var records = await db.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);

    if (HasVersionConflict(request, ids, records))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
      var ctx = new GridBatchContext<TEntity>(ids, records, ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, cancellationToken);
      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(cancellationToken);
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, records, MessageType.Error)
        };
      }
      await db.SaveChangesAsync(cancellationToken);
      await tx.CommitAsync(cancellationToken);
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId
      };
    });
  }

  public virtual async Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default)
  {
    ParentId = request.ParentId;
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
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }] };
    }

    var selectedIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    if (selectedIds.Count == 0)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No records selected.", Type = MessageType.Error }] };
    }

    object? input = transition.Ask?.Read(body);
    if (transition.Ask != null)
    {
      var askFailures = transition.Ask.Validate(input, L);
      if (askFailures.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [.. askFailures]
        };
      }
    }

    var entities = await db.Set<TEntity>().Where(e => selectedIds.Contains(e.Id)).ToListAsync(ct);
    if (HasVersionConflict(request, selectedIds, entities))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var context = new MutationContext(db);
      await statusFlow.MoveAsync(context, entities, transition.Name, input, ct);

      if (context.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(context.Errors, entities)
        };
      }

      await tx.CommitAsync(ct);
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    });
  }

  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentId = request.ParentId;
    if (IsReadOnly || (User != null && User.IsAuthenticated && !User.Can(Feature.Delete) && !User.Can(Feature.All)))
    {
      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Refused,
        Deleted = 0,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

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
    if (HasVersionConflict(request, ids, entities))
    {
      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Conflict,
        Deleted = 0,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

    var hooks = new Hooks();
    SetHooks(hooks);

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var ctx = new GridDeleteContext<TEntity>(ids, entities, token => db.SaveChangesAsync(token), db);

      foreach (var h in hooks.BeforeDeleteActions)
      {
        await h(ctx, ct);
      }

      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = FormatErrors(ctx.Errors, entities)
        };
      }

      try
      {
        db.Set<TEntity>().RemoveRange(entities);
        await db.SaveChangesAsync(ct);

        foreach (var h in hooks.AfterDeleteActions)
        {
          await h(ctx, ct);
        }
        if (ctx.Errors.Count > 0)
        {
          await tx.RollbackAsync(ct);
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Warnings = FormatErrors(ctx.Errors, entities)
          };
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
      }
      catch (DbUpdateException)
      {
        await tx.RollbackAsync(ct);
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = [new MessageDto { Detail = L["DeleteRefused"], Type = MessageType.Error }]
        };
      }

      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Committed,
        Deleted = entities.Count
      };
    });
  }

  private static bool HasVersionConflict(GridRequest request, IReadOnlyList<long> ids, IReadOnlyList<TEntity> records)
  {
    if (request.SelectedVersions is not { Count: > 0 } versions || versions.Count != ids.Count)
    {
      return false;
    }

    var recordMap = records.ToDictionary(r => r.Id);
    for (int i = 0; i < ids.Count; i++)
    {
      if (!recordMap.TryGetValue(ids[i], out var rec))
      {
        return true;
      }
      if (rec is IHasVersion versioned && versions[i] > 0 && versioned.Ver != versions[i])
      {
        return true;
      }
    }

    return false;
  }

  private List<MessageDto> FormatErrors(IEnumerable<MutationError> errors, IReadOnlyList<TEntity> records, string defaultType = MessageType.Warning)
  {
    var queryDeclaration = new Query();
    SetQuery(queryDeclaration);
    var compiledLabel = queryDeclaration.LabelSelector?.Compile();
    var byId = records.ToDictionary(r => r.Id);

    return errors.Select(e =>
    {
      var msg = e.ToMessage(defaultType);
      if (e.RecordId.HasValue && string.IsNullOrEmpty(msg.RecordLabel) && byId.TryGetValue(e.RecordId.Value, out var rec))
      {
        string? label = null;
        if (compiledLabel != null)
        {
          try { label = compiledLabel.DynamicInvoke(rec)?.ToString(); } catch { }
        }
        label ??= (PropertyAccessorCache.GetValue(rec, "Number")
          ?? PropertyAccessorCache.GetValue(rec, "Name")
          ?? PropertyAccessorCache.GetValue(rec, "Title"))?.ToString();
        msg.RecordLabel = !string.IsNullOrWhiteSpace(label) ? label : $"#{e.RecordId.Value}";
      }
      return msg;
    }).ToList();
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
    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

    if (queryDeclaration.Selector != null)
    {
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
      var defaultDataProvider = DataProvider.Create(query, queryMap);
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
