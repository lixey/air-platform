using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridMutationRunner
{
  public static async Task<IReadOnlyList<long>> InferBoundParentIdFromIdsAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool includeQueryFilters = false)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (grid.ParentId is > 0 || ids.Count == 0 || grid.GetParentDeclaration().BoundProperty is not { } boundProp)
    {
      return [];
    }

    var probeQuery = grid.InvokeBaseQuery(grid.DbContext).AsNoTracking();
    var restrictions = new GridService<TEntity, TRow>.UserRestrictions();
    grid.InvokeSetUserRestrictions(restrictions);
    foreach (var restriction in restrictions.Filters)
    {
      probeQuery = probeQuery.Where(restriction);
    }

    if (includeQueryFilters)
    {
      foreach (var filter in grid.GetQueryDeclaration().Filters)
      {
        probeQuery = probeQuery.Where(filter);
      }
    }

    var probeRows = await probeQuery.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
    if (probeRows.Count == 0 || includeQueryFilters && probeRows.Count != ids.Distinct().Count())
    {
      return [];
    }

    var distinctParentIds = probeRows
      .Select(e => boundProp.GetValue(e) is { } raw ? Convert.ToInt64(raw, CultureInfo.InvariantCulture) : 0L)
      .Where(pid => pid > 0)
      .Distinct()
      .OrderBy(pid => pid)
      .ToList();
    if (distinctParentIds.Count == 1)
    {
      grid.ParentId = distinctParentIds[0];
    }

    return distinctParentIds;
  }

  public static async Task<(List<TEntity> Entities, List<MessageDto> MissingErrors)> LoadSelectedEntitiesAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool asTracking = false,
    bool lockEntities = false)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (ids.Count == 0)
    {
      return ([], []);
    }

    var db = grid.DbContext;
    var parent = grid.GetParentDeclaration();
    if (grid.ParentId is null or <= 0 && parent.BoundProperty != null)
    {
      var distinctParentIds = await grid.InferBoundParentIdInternalAsync(ids, ct, includeQueryFilters: true);
      if (distinctParentIds.Count == 1)
      {
        if (!await grid.EnsureParentScopeInternalAsync(lockParent: lockEntities && db.Database.CurrentTransaction != null, ct))
        {
          return ([], [new MessageDto { Code = "grid.delete.unavailable", Detail = grid.L["GridDeleteUnavailable"], Type = MessageType.Error }]);
        }
      }
      else if (distinctParentIds.Count > 1 && lockEntities && db.Database.CurrentTransaction != null && parent.BoundParentEntityType is { } parentEntityType)
      {
        await MutationContext.AcquireLocksAsync(db, parentEntityType, distinctParentIds.Select(pid => new[] { pid }).ToList(), ct);
      }
    }

    if (lockEntities && db.Database.CurrentTransaction != null)
    {
      var sortedLockKeys = ids.Distinct().OrderBy(id => id).Select(id => new[] { id }).ToList();
      await MutationContext.AcquireLocksAsync(db, typeof(TEntity), sortedLockKeys, ct);
      if (asTracking && db.Database.IsRelational())
      {
        var idSet = ids.ToHashSet();
        foreach (var local in db.Set<TEntity>().Local.Where(e => idSet.Contains(e.Id)).ToList())
        {
          if (db.Entry(local).State == EntityState.Unchanged)
          {
            await db.Entry(local).ReloadAsync(ct);
          }
        }
      }
    }

    var baseQuery = grid.RestrictedQueryInternal();
    if (asTracking)
    {
      baseQuery = baseQuery.AsTracking();
    }

    var rows = await baseQuery.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
    var byId = rows.ToDictionary(e => e.Id);
    var entities = new List<TEntity>(rows.Count);
    var missing = new List<MessageDto>();
    foreach (var id in ids)
    {
      if (byId.TryGetValue(id, out var ent))
      {
        entities.Add(ent);
      }
      else
      {
        missing.Add(new MessageDto
        {
          Code = "grid.delete.unavailable",
          Detail = grid.L["GridDeleteUnavailable"],
          RecordId = id,
          Type = MessageType.Error
        });
      }
    }

    return (entities, missing);
  }

  public static async Task<GridDeleteDto> DeleteAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var db = grid.DbContext;
    List<TEntity>? deletedRecords = null;
    var servicePath = ServicePathResolver.Resolve(grid.GetType());
    var actionPath = $"{servicePath}/delete";

    var result = await MutationRunner.RunIdempotentMutationAsync<GridDeleteDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      request.RawPayload,
      async mutationCt =>
      {
        var (ids, versions, versionCountMismatch) = GridActionRunner.NormalizeSelection(request);

        await grid.InferBoundParentIdInternalAsync(ids, mutationCt);
        if (!await grid.EnsureParentScopeInternalAsync(lockParent: true, mutationCt) || !grid.CanDelete())
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (ids.Count == 0)
        {
          return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
        }

        var hooks = grid.GetHooksDeclaration();
        if (!hooks.IsMultiDeleteAllowed && ids.Count > 1)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var (entities, missing) = await LoadSelectedEntitiesAsync(grid, ids, mutationCt, asTracking: true, lockEntities: true);
        if (missing.Count > 0)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = missing
          };
        }

        if (!grid.CanDelete())
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (HasVersionConflict(versions, versionCountMismatch, ids, entities))
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Conflict,
            Deleted = 0,
            Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }

        var ctx = new GridDeleteContext<TEntity>(ids, entities, token => db.SaveChangesAsync(token), db);

        foreach (var h in hooks.BeforeDeleteActions)
        {
          await h(ctx, mutationCt);
        }

        if (ctx.Errors.Count > 0)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = FormatErrors(grid, ctx.Errors, entities, MessageType.Error)
          };
        }

        List<AffectedDto>? affected;
        try
        {
          db.Set<TEntity>().RemoveRange(entities);
          await db.SaveChangesAsync(mutationCt);

          if (hooks.AfterDeleteActions.Count > 0)
          {
            foreach (var h in hooks.AfterDeleteActions)
            {
              await h(ctx, mutationCt);
            }

            if (ctx.Errors.Count > 0)
            {
              return new GridDeleteDto
              {
                Outcome = MutationOutcome.Refused,
                Deleted = 0,
                Messages = FormatErrors(grid, ctx.Errors, entities, MessageType.Error)
              };
            }

            if (db.ChangeTracker.HasChanges())
            {
              await db.SaveChangesAsync(mutationCt);
            }
          }

          affected = await RecalculateBoundParentsForEntitiesAsync(grid, entities, mutationCt);
        }
        catch (DbUpdateException)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = grid.L["DeleteRefused"], Type = MessageType.Error }]
          };
        }

        deletedRecords = entities;

        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Committed,
          Deleted = entities.Count,
          Warnings = ctx.Warnings.Count > 0 ? FormatErrors(grid, ctx.Warnings, entities, MessageType.Warning) : null,
          Affected = affected
        };
      },
      ct,
      grid.PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && grid.PlatformApp != null && deletedRecords is { Count: > 0 })
    {
      await grid.PlatformApp.ClearAuthCacheForEntitiesAsync(deletedRecords, db, ct);
    }

    return result;
  }

  public static Task<List<AffectedDto>?> RecalculateBoundParentsForEntitiesAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IReadOnlyList<TEntity> records,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var parent = grid.GetParentDeclaration();
    if (parent.BoundProperty == null && parent.BoundParentServiceType == null)
    {
      return Task.FromResult<List<AffectedDto>?>(null);
    }

    var parentIds = new List<long?> { grid.ParentId };
    if (parent.BoundProperty != null)
    {
      foreach (var rec in records)
      {
        parentIds.Add(parent.BoundProperty.GetValue(rec) as long?);
      }
    }

    return ParentScopeValidator.RecalculateBoundParentsAsync(
      grid.DbContext,
      grid.PlatformApp,
      grid.ParentService,
      parent.BoundParentService,
      parent.BoundParentServiceType,
      parentIds,
      ct,
      requestParentId: grid.ParentId);
  }

  public static bool HasVersionConflict<TEntity>(
    IReadOnlyList<int>? versions,
    bool versionCountMismatch,
    IReadOnlyList<long> ids,
    IReadOnlyList<TEntity> records)
    where TEntity : class, IEntity
  {
    if (versionCountMismatch)
    {
      return true;
    }

    if (versions is not { Count: > 0 })
    {
      return false;
    }

    var recordMap = records.ToDictionary(r => r.Id);
    for (var i = 0; i < ids.Count; i++)
    {
      if (recordMap.TryGetValue(ids[i], out var rec) && rec is IHasVersion versioned && versions[i] > 0 && versioned.Ver != versions[i])
      {
        return true;
      }
    }

    return false;
  }

  public static List<MessageDto> FormatErrors<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IEnumerable<MutationError> errors,
    IReadOnlyList<TEntity> records,
    string defaultType = MessageType.Error)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var queryDeclaration = grid.GetQueryDeclaration();
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
          try
          {
            label = compiledLabel.DynamicInvoke(rec)?.ToString();
          }
          catch { }
        }

        label ??= (PropertyAccessorCache.GetValue(rec, "Number")
          ?? PropertyAccessorCache.GetValue(rec, "Name")
          ?? PropertyAccessorCache.GetValue(rec, "Title"))?.ToString();
        msg.RecordLabel = !string.IsNullOrWhiteSpace(label) ? label : $"#{e.RecordId.Value}";
      }

      return msg;
    }).ToList();
  }

  public static async Task<RowDto?> ReadRowAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    long id,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var queryDeclaration = grid.GetQueryDeclaration();
    var cols = grid.CreateColumnsDeclaration();
    var query = grid.RestrictedQueryInternal().AsNoTracking().Where(e => e.Id == id);

    if (queryDeclaration.Selector != null)
    {
      var row = await query.Select(queryDeclaration.Selector).FirstOrDefaultAsync(ct);
      if (row is null)
      {
        return null;
      }

      if (queryDeclaration.ComputeAction != null)
      {
        await queryDeclaration.ComputeAction([row], ct);
      }

      var shaped = grid.InvokeShapeRow(row, cols.List, queryDeclaration.RowFlags);
      await GridRowEnricher.PopulateCoversAsync(grid, [shaped], cols.List, ct);
      await GridRowEnricher.PopulateProjectedColorsAsync(grid, [shaped], cols.List, ct);
      await GridRowEnricher.PopulateRowAvailabilityAsync(grid, [shaped], cols, ct);
      return shaped;
    }

    var defaultSelector = GridRowEnricher.CanUseDefaultProjection(grid, query, queryDeclaration)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(cols.List, grid.HasCustomShapeRow)
      : null;
    var entity = defaultSelector != null
      ? await query.Select(defaultSelector).FirstOrDefaultAsync(ct)
      : await GridRowEnricher.IncludeColumnNavigations<TEntity, TRow>(query, cols.List).FirstOrDefaultAsync(ct);
    if (entity is null)
    {
      return null;
    }

    if (queryDeclaration.ComputeAction != null && entity is TRow typedRow)
    {
      await queryDeclaration.ComputeAction([typedRow], ct);
    }

    var shapedEntity = grid.InvokeShapeRow((entity as TRow)!, cols.List, queryDeclaration.RowFlags);
    List<RowDto> singleShapedList = [shapedEntity];
    if (queryDeclaration.RowComputeAction != null)
    {
      await GridRowShaper.InvokeRowComputeActionAsync([entity], singleShapedList, cols.List, queryDeclaration.RowComputeAction, ct);
    }

    await GridRowEnricher.PopulateComputedColumnsAsync(grid, singleShapedList, cols.List, ct);
    await GridRowEnricher.PopulateCoversAsync(grid, singleShapedList, cols.List, ct);
    await GridRowEnricher.PopulateProjectedColorsAsync(grid, singleShapedList, cols.List, ct);
    await GridRowEnricher.PopulateRowAvailabilityAsync(grid, singleShapedList, cols, ct);
    return shapedEntity;
  }

  public static async Task<GridCellDto> SaveCellAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridCellRequest request,
    IServiceProvider services,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    if (request.Id is not > 0)
    {
      throw new KeyNotFoundException(Localizer.Instance["FormNotFound"]);
    }

    await grid.InferBoundParentIdInternalAsync([request.Id.Value], ct);
    if (!await grid.EnsureParentScopeInternalAsync(lockParent: false, ct))
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var cols = grid.CreateColumnsDeclaration();

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase))
              ?? cols.List.FirstOrDefault(c => !string.IsNullOrEmpty(c.FilterPath) && string.Equals(c.FilterPath, request.Path, StringComparison.OrdinalIgnoreCase));
    if (grid.IsGridReadOnly ||
        col == null ||
        !col.IsEditable ||
        string.IsNullOrEmpty(col.Path) ||
        col.Path.Contains('.') ||
        !await grid.RestrictedQueryInternal().AsNoTracking().AnyAsync(e => e.Id == request.Id.Value, ct))
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var formType = grid.ResolveFormServiceType(cols);
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      var form = (IFormService)services.GetRequiredService(formType);
      if (grid.PlatformApp != null)
      {
        form.AttachPlatformApp(grid.PlatformApp);
      }

      var formReq = new FormRequest
      {
        Id = request.Id,
        Ver = request.Ver,
        ParentId = grid.ParentId,
        ParentService = grid.ParentService ?? grid.GetParentDeclaration().BoundParentService
      };
      try
      {
        return await form.SaveCellAsync(request.Id.Value, request.Path, request.Value, formReq, token => grid.ReadRowAsync(request.Id.Value, token), ct);
      }
      catch (JsonException)
      {
        return new GridCellDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = grid.L["FormValidationInvalid"], Path = request.Path, Type = MessageType.Error }]
        };
      }
    }

    return new GridCellDto
    {
      Outcome = MutationOutcome.Refused,
      Messages = [new MessageDto { Detail = grid.L["FormReadOnly"], Path = request.Path, Type = MessageType.Error }]
    };
  }

  public static async Task<IResult> ExportAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var parentScopeValid = await grid.EnsureParentScopeInternalAsync(lockParent: false, ct);
    var cols = grid.CreateColumnsDeclaration();
    GridScreenBuilder.LocalizeAndConfigureColumns(grid, cols.List);
    var exportColumns = cols.List.Where(c => !c.IsHidden && !string.IsNullOrWhiteSpace(c.Path) && c.Cell is not GridCellCoverModel).ToList();

    const int limit = 100_000;
    IAsyncEnumerable<RowDto> rowStream;
    if (!parentScopeValid)
    {
      rowStream = AsyncEnumerable.Empty<RowDto>();
    }
    else
    {
      var query = GridQueryExecutor.BuildFilteredEntityQuery(grid, request, out _, out var queryDeclaration);
      if (request.SelectedIds is { Count: > 0 } selectedIds)
      {
        query = query.Where(e => selectedIds.Contains(e.Id));
      }

      if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
      {
        request.Sort = queryDeclaration.DefaultSort.ToList();
      }

      GridQueryExecutor.NormalizeGridRequest(request, request, queryDeclaration, isSelect: false, applyInitialFilters: false);

      var queryMap = GridService<TEntity, TRow>.BuildQueryMap(grid.GetType(), queryDeclaration, cols.List, grid.IsSqlServerProvider);

      if (queryDeclaration.Selector != null)
      {
        var dataProvider = DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector);
        rowStream = queryDeclaration.ComputeAction != null
          ? GridRowShaper.ShapeComputedChunksAsync(grid, dataProvider.StreamChunksAsync(request, limit + 1, 500, ct), queryDeclaration.ComputeAction, exportColumns, ct)
          : GridRowShaper.ShapeStreamAsync(grid, dataProvider.StreamAsync(request, limit + 1, ct), exportColumns, ct);
      }
      else
      {
        var exportSelector = GridRowEnricher.CanUseDefaultProjection(grid, query, queryDeclaration)
          ? GridProjectionBuilder.TryBuildSelector<TEntity>(exportColumns, grid.HasCustomShapeRow)
          : null;
        var defaultDataProvider = exportSelector != null
          ? DataProvider.Create(query.AsNoTracking(), queryMap, exportSelector)
          : DataProvider.Create(GridRowEnricher.IncludeColumnNavigations<TEntity, TRow>(query.AsNoTracking(), exportColumns), queryMap);
        var hasSubqueryCols = exportColumns.Any(c => !c.IsHidden && c.IsSubqueryExpression && c.ValueExpression != null);
        rowStream = queryDeclaration.ComputeAction != null || queryDeclaration.RowComputeAction != null || hasSubqueryCols
          ? GridRowShaper.ShapeEntityChunksAsync(
            grid,
            defaultDataProvider.StreamChunksAsync(request, limit + 1, 500, ct),
            exportColumns,
            queryDeclaration.ComputeAction,
            queryDeclaration.RowComputeAction,
            hasSubqueryCols,
            ct)
          : GridRowShaper.ShapeEntityStreamAsync(grid, defaultDataProvider.StreamAsync(request, limit + 1, ct), exportColumns, ct);
      }
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    try
    {
      var label = grid.L.TryGetString(grid.Feature.Name, out var l) ? l : (string.IsNullOrEmpty(grid.Feature.Name) ? typeof(TEntity).Name : grid.Feature.Name);
      var cleanName = Regex.Replace(label, @"[\s\W]+", "_").Trim('_');
      TimeZones.TryFind(grid.User?.TimeZone, out var exportTimeZone);
      var fileName = $"{cleanName}_{TimeZoneInfo.ConvertTime(grid.TimeClock.UtcNow, exportTimeZone):yyyyMMdd_HHmm}.xlsx";
      await GridExcelWriter.WriteAsync(fileStream, label, exportColumns, col => col.MemberType ?? PropertyAccessorCache.GetProperty(typeof(TRow), col.Path ?? "")?.PropertyType, rowStream, grid.L, exportTimeZone, limit, grid.L["ExportTruncated", limit], ct);
      fileStream.Position = 0;
      return Results.File(fileStream, GridExcelWriter.ContentType, fileName);
    }
    catch
    {
      await fileStream.DisposeAsync();
      throw;
    }
  }
}
