using Platform.App.Grid.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  protected async Task<IReadOnlyList<long>> InferBoundParentIdFromIdsAsync(
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool includeQueryFilters = false)
  {
    if (ParentId is > 0 || ids.Count == 0 || GetParent().BoundProperty is not { } boundProp)
    {
      return [];
    }

    var probeQuery = BaseQuery(db).AsNoTracking();
    var restrictions = BuildRestrictions();
    foreach (var restriction in restrictions.Filters)
    {
      probeQuery = probeQuery.Where(restriction);
    }

    if (includeQueryFilters)
    {
      foreach (var filter in GetQueryDeclaration().Filters)
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
      ParentId = distinctParentIds[0];
    }

    return distinctParentIds;
  }

  private async Task<(List<TEntity> Entities, List<MessageDto> MissingErrors)> LoadSelectedEntitiesAsync(
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool asTracking = false,
    bool lockEntities = false,
    bool reloadTracked = false)
  {
    if (ids.Count == 0)
    {
      return ([], []);
    }

    var parent = GetParent();
    if (ParentId is null or <= 0 && parent.BoundProperty != null)
    {
      var distinctParentIds = await InferBoundParentIdFromIdsAsync(ids, ct, includeQueryFilters: true);
      if (distinctParentIds.Count == 1)
      {
        if (!await EnsureParentScopeAsync(lockParent: lockEntities && db.Database.CurrentTransaction != null, ct))
        {
          return ([], [new MessageDto { Code = "grid.delete.unavailable", Detail = L["GridDeleteUnavailable"], Type = MessageType.Error }]);
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
      reloadTracked = true;
    }

    if (reloadTracked && asTracking && db.Database.CurrentTransaction != null && db.Database.IsRelational())
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

    var baseQuery = RestrictedQuery();
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
          Detail = L["GridDeleteUnavailable"],
          RecordId = id,
          Type = MessageType.Error
        });
      }
    }

    return (entities, missing);
  }

  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    List<TEntity>? deletedRecords = null;
    var postCommitFileCleanups = new List<Func<IServiceProvider?, CancellationToken, Task>>();
    var servicePath = ServicePathResolver.Resolve(GetType());
    var actionPath = $"{servicePath}/delete";

    var result = await MutationRunner.RunIdempotentMutationAsync<GridDeleteDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      request.RawPayload,
      async mutationCt =>
      {
        postCommitFileCleanups.Clear();
        var (ids, versions, versionCountMismatch) = NormalizeSelection(request);

        await InferBoundParentIdFromIdsAsync(ids, mutationCt);
        if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || !CanDelete())
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (ids.Count == 0)
        {
          return new GridDeleteDto { Outcome = MutationOutcome.Committed, Deleted = 0 };
        }

        var hooks = GetHooks();
        if (!hooks.IsMultiDeleteAllowed && ids.Count > 1)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var (entities, missing) = await LoadSelectedEntitiesAsync(ids, mutationCt, asTracking: true, lockEntities: true);
        if (missing.Count > 0)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = missing
          };
        }

        if (!CanDelete())
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (HasVersionConflict(versions, versionCountMismatch, ids, entities))
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Conflict,
            Deleted = 0,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
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
            Messages = FormatErrors(ctx.Errors, entities, MessageType.Error)
          };
        }

        List<AffectedDto>? affected;
        try
        {
          foreach (var deleteFiles in hooks.DeleteFileActions)
          {
            await deleteFiles(ctx, postCommitFileCleanups, mutationCt);
          }

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
                Messages = FormatErrors(ctx.Errors, entities, MessageType.Error)
              };
            }

            if (db.ChangeTracker.HasChanges())
            {
              await db.SaveChangesAsync(mutationCt);
            }
          }

          affected = await RecalculateBoundParentsForEntitiesAsync(entities, mutationCt);
        }
        catch (DbUpdateException)
        {
          return new GridDeleteDto
          {
            Outcome = MutationOutcome.Refused,
            Deleted = 0,
            Messages = [new MessageDto { Detail = L["DeleteRefused"], Type = MessageType.Error }]
          };
        }

        deletedRecords = entities;

        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Committed,
          Deleted = entities.Count,
          Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, entities, MessageType.Warning) : null,
          Affected = affected
        };
      },
      ct,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed)
    {
      foreach (var cleanup in postCommitFileCleanups)
      {
        await cleanup(PlatformApp?.Services, CancellationToken.None);
      }

      if (PlatformApp != null && deletedRecords is { Count: > 0 })
      {
        await PlatformApp.ClearAuthCacheForEntitiesAsync(deletedRecords, db, ct);
      }
    }

    return result;
  }

  private Task<List<AffectedDto>?> RecalculateBoundParentsForEntitiesAsync(
    IReadOnlyList<TEntity> records,
    CancellationToken ct)
  {
    var parent = GetParent();
    if (parent.BoundProperty == null && parent.BoundParentServiceType == null)
    {
      return Task.FromResult<List<AffectedDto>?>(null);
    }

    var parentIds = new List<long?> { ParentId };
    if (parent.BoundProperty != null)
    {
      foreach (var rec in records)
      {
        parentIds.Add(parent.BoundProperty.GetValue(rec) as long?);
      }
    }

    return BoundParentRecalculator.RecalculateBoundParentsAsync(
      db,
      PlatformApp,
      ParentService,
      parent.BoundParentService,
      parent.BoundParentServiceType,
      parentIds,
      ct,
      requestParentId: ParentId);
  }

  private static bool HasVersionConflict(
    IReadOnlyList<int>? versions,
    bool versionCountMismatch,
    IReadOnlyList<long> ids,
    IReadOnlyList<TEntity> records)
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

  private List<MessageDto> FormatErrors(
    IEnumerable<MutationError> errors,
    IReadOnlyList<TEntity> records,
    string defaultType = MessageType.Error)
  {
    var compiledLabel = GetQueryDeclaration().CompiledLabelSelector;
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
            label = compiledLabel(rec);
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

  public virtual async Task<RowDto?> ReadRowAsync(long id, CancellationToken ct = default)
  {
    var queryDeclaration = GetQueryDeclaration();
    var cols = await CreateColumnsAsync(ct);
    var query = RestrictedQuery().AsNoTracking().Where(e => e.Id == id);

    if (queryDeclaration.Selector != null)
    {
      var row = await query.Select(queryDeclaration.Selector).FirstOrDefaultAsync(ct);
      if (row is null)
      {
        return null;
      }

      var shaped = await GridRowShaper.ShapeAndEnrichBatchAsync(
        this,
        [row],
        cols.List,
        cols,
        queryDeclaration,
        includeScreenEnrichments: true,
        ct);
      return shaped[0];
    }

    var defaultSelector = GridRowEnricher.CanUseDefaultProjection(this, query, queryDeclaration)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(cols.List, HasCustomShapeRow)
      : null;
    var entity = defaultSelector != null
      ? await query.Select(defaultSelector).FirstOrDefaultAsync(ct)
      : await GridRowEnricher.IncludeColumnNavigations<TEntity, TRow>(query, cols.List).FirstOrDefaultAsync(ct);
    if (entity is null)
    {
      return null;
    }

    var shapedEntity = await GridRowShaper.ShapeAndEnrichBatchAsync(
      this,
      [entity],
      cols.List,
      cols,
      queryDeclaration,
      includeScreenEnrichments: true,
      ct);
    return shapedEntity[0];
  }

  public virtual async Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (request.Id is not > 0)
    {
      throw new KeyNotFoundException(L["FormNotFound"]);
    }

    await InferBoundParentIdFromIdsAsync([request.Id.Value], ct);
    if (!await EnsureParentScopeAsync(lockParent: false, ct))
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var cols = await CreateColumnsAsync(ct);

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase))
              ?? cols.List.FirstOrDefault(c => !string.IsNullOrEmpty(c.FilterPath) && string.Equals(c.FilterPath, request.Path, StringComparison.OrdinalIgnoreCase));
    if (IsReadOnly ||
        col == null ||
        !col.IsEditable ||
        string.IsNullOrEmpty(col.Path) ||
        col.Path.Contains('.'))
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var formType = ResolveFormServiceType(cols);
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      var form = (IFormService)services.GetRequiredService(formType);
      form.BindContext(PlatformApp);

      var formReq = new FormRequest
      {
        Id = request.Id,
        Ver = request.Ver,
        ParentId = ParentId,
        ParentService = ParentService ?? GetParent().BoundParentService,
        IdempotencyKey = request.IdempotencyKey,
        RawPayload = request.RawPayload
      };
      try
      {
        return await form.SaveCellAsync(
          request.Id.Value,
          request.Path,
          request.Value,
          formReq,
          token => ReadRowAsync(request.Id.Value, token),
          token => RestrictedQuery().AsNoTracking().AnyAsync(e => e.Id == request.Id.Value, token),
          ct);
      }
      catch (JsonException)
      {
        return new GridCellDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = L["FormValidationInvalid"], Path = request.Path, Type = MessageType.Error }]
        };
      }
    }

    return new GridCellDto
    {
      Outcome = MutationOutcome.Refused,
      Messages = [new MessageDto { Detail = L["FormReadOnly"], Path = request.Path, Type = MessageType.Error }]
    };
  }

  public virtual async Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    var parentScopeValid = await EnsureParentScopeAsync(lockParent: false, ct);
    var cols = await CreateColumnsAsync(ct);
    var exportColumns = ProjectScreenColumns(
      cols.List.Where(c => !c.IsHidden && !string.IsNullOrWhiteSpace(c.Path) && c.Cell is not GridCellCoverModel));

    const int limit = 100_000;
    IAsyncEnumerable<RowDto> rowStream;
    if (!parentScopeValid)
    {
      rowStream = AsyncEnumerable.Empty<RowDto>();
    }
    else
    {
      var query = BuildFilteredEntityQuery(request, out _, out var queryDeclaration);
      if (request.SelectedIds is { Count: > 0 } selectedIds)
      {
        query = query.Where(e => selectedIds.Contains(e.Id));
      }

      if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
      {
        request.Sort = queryDeclaration.DefaultSort.ToList();
      }

      NormalizeGridRequest(request, request, queryDeclaration, isSelect: false, applyInitialFilters: false);

      var queryMap = BuildQueryMap(GetType(), queryDeclaration, cols.List, IsSqlServerProvider);

      if (queryDeclaration.Selector != null)
      {
        var dataProvider = DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector);
        rowStream = GridRowShaper.StreamShapedChunksAsync(
          this,
          dataProvider.StreamChunksAsync(request, limit + 1, 500, ct),
          exportColumns,
          cols,
          queryDeclaration,
          ct);
      }
      else
      {
        var exportSelector = GridRowEnricher.CanUseDefaultProjection(this, query, queryDeclaration)
          ? GridProjectionBuilder.TryBuildSelector<TEntity>(exportColumns, HasCustomShapeRow)
          : null;
        var defaultDataProvider = exportSelector != null
          ? DataProvider.Create(query.AsNoTracking(), queryMap, exportSelector)
          : DataProvider.Create(GridRowEnricher.IncludeColumnNavigations<TEntity, TRow>(query.AsNoTracking(), exportColumns), queryMap);
        rowStream = GridRowShaper.StreamShapedChunksAsync(
          this,
          defaultDataProvider.StreamChunksAsync(request, limit + 1, 500, ct),
          exportColumns,
          cols,
          queryDeclaration,
          ct);
      }
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    try
    {
      var label = L.TryGetString(Feature.Name, out var l) ? l : (string.IsNullOrEmpty(Feature.Name) ? typeof(TEntity).Name : Feature.Name);
      var cleanName = Regex.Replace(label, @"[\s\W]+", "_").Trim('_');
      TimeZones.TryFind(User?.TimeZone, out var exportTimeZone);
      var fileName = $"{cleanName}_{TimeZoneInfo.ConvertTime(Time.UtcNow, exportTimeZone):yyyyMMdd_HHmm}.xlsx";
      await GridExcelWriter.WriteAsync(fileStream, label, exportColumns, col => GridColumnRuntime.TryGet(col)?.MemberType ?? PropertyAccessorCache.GetProperty(typeof(TRow), col.Path ?? "")?.PropertyType, rowStream, L, exportTimeZone, limit, L["ExportTruncated", limit], ct);
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
