using Platform.App.Grid.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected IQueryable<TEntity> RestrictedQuery()
  {
    var query = BuildBaseEntityQuery(null, out _, out var queryDeclaration, preserveParent: true);
    if (queryDeclaration.LabelSelector != null &&
        ExtractNavigationPath(queryDeclaration.LabelSelector.Body) is { } labelNav &&
        !string.IsNullOrWhiteSpace(labelNav))
    {
      query = query.Include(labelNav);
    }

    return query;
  }

  private static string? ExtractNavigationPath(System.Linq.Expressions.Expression expr)
  {
    if (expr is System.Linq.Expressions.UnaryExpression ue)
    {
      expr = ue.Operand;
    }

    var parts = new List<string>();
    while (expr is System.Linq.Expressions.MemberExpression me)
    {
      parts.Insert(0, me.Member.Name);
      expr = me.Expression!;
    }

    return parts.Count > 1 ? string.Join('.', parts.Take(parts.Count - 1)) : null;
  }

  private async Task<(List<TEntity> Entities, List<MessageDto> MissingErrors)> LoadSelectedEntitiesAsync(
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool asTracking = false,
    bool lockEntities = false)
  {
    if (ids.Count == 0)
    {
      return ([], []);
    }

    var parent = GetParent();
    if (ParentId is null or <= 0 && parent.BoundProperty is { } boundProp)
    {
      var probeRows = await RestrictedQuery().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
      if (probeRows.Count == ids.Distinct().Count())
      {
        var distinctParentIds = probeRows
          .Select(e => boundProp.GetValue(e) as long?)
          .Where(pid => pid is > 0)
          .Select(pid => pid!.Value)
          .Distinct()
          .OrderBy(pid => pid)
          .ToList();
        if (distinctParentIds.Count == 1)
        {
          ParentId = distinctParentIds[0];
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
    }

    if (lockEntities && db.Database.CurrentTransaction != null)
    {
      await MutationContext.AcquireLocksAsync(db, typeof(TEntity), ids.Select(id => new[] { id }).ToList(), ct);
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

  protected Task<GridActionDto> ExecuteSingleAsync(
    GridRequest request,
    Func<GridBatchContext<TEntity>, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    var ids = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : new List<long>());
    if (ids.Count != 1)
    {
      return Task.FromResult(new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      });
    }

    return ExecuteBatchAsync(
      request,
      (ctx, ct) => handler(ctx, ctx.Records[0], ct),
      cancellationToken,
      actionName
    );
  }

  protected Task<GridActionDto> ExecuteBatchAsync(
    GridRequest request,
    IReadOnlyList<long> selectedIds,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    request.SelectedIds = [.. selectedIds];
    request.Id = null;
    return ExecuteBatchAsync(request, handler, cancellationToken, actionName);
  }

  protected async Task<GridActionDto> ExecuteBatchAsync(
    GridRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    List<TEntity>? batchRecords = null;

    var result = await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      var nav = new Nav(localizer: L);
      SetNav(nav);

      var ids = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : new List<long>());
      SelectedIds = ids;
      NavActionEntry? navAction = null;
      if (!string.IsNullOrEmpty(actionName))
      {
        navAction = nav.Actions.FirstOrDefault(a => string.Equals(a.Model.Method, actionName, StringComparison.OrdinalIgnoreCase));
        if (navAction?.Model.IsSingle == true && ids.Count != 1)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }
      }

      if (IsReadOnly && navAction?.IsAllowedInReadOnly != true)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (navAction?.Ask != null)
      {
        var askFailures = navAction.Ask.Validate(request, L);
        if (askFailures.Count > 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [.. askFailures]
          };
        }
      }

      var (records, missing) = await LoadSelectedEntitiesAsync(ids, mutationCt, asTracking: true, lockEntities: true);
      if (missing.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = missing
        };
      }

      if (navAction is { Offers.Count: > 0 } && records.Count > 0)
      {
        var recordIds = records.Select(r => r.Id).ToList();
        foreach (var offer in navAction.Offers)
        {
          if (offer.Condition is Expression<Func<TEntity, bool>> cond)
          {
            var passedIds = (await db.Set<TEntity>()
              .Where(e => recordIds.Contains(e.Id))
              .Where(cond)
              .Select(e => e.Id)
              .ToListAsync(mutationCt))
              .ToHashSet();

            foreach (var record in records)
            {
              if (!passedIds.Contains(record.Id))
              {
                return new GridActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Warnings = FormatErrors([new MutationError("grid.availability", L[offer.Reason], RecordId: record.Id)], records, MessageType.Error)
                };
              }
            }
          }
        }
      }

      if (HasVersionConflict(request, ids, records))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Conflict,
          Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
        };
      }

      var ctx = new GridBatchContext<TEntity>(ids, records, ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, records, MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      batchRecords = records;
      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntitiesAsync(records, db, mutationCt);
      }

      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, records, MessageType.Warning) : null
      };
    }, ct: cancellationToken);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && batchRecords is { Count: > 0 })
    {
      await PlatformApp.ClearAuthCacheForEntitiesAsync(batchRecords, db, cancellationToken);
    }

    return result;
  }

  public virtual async Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      var cols = CreateColumns();
      if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
      {
        return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No status flow configured for this grid.", Type = MessageType.Error }] };
      }

      if (IsReadOnly && !cols.StatusFlowAllowsReadOnly)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
      {
        flowSvc.AttachPlatformApp(PlatformApp);
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

      var input = transition.Ask?.Read(body);
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

      var (entities, missing) = await LoadSelectedEntitiesAsync(selectedIds, mutationCt, asTracking: true, lockEntities: true);
      if (missing.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = missing
        };
      }

      if (HasVersionConflict(request, selectedIds, entities))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Conflict,
          Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
        };
      }

      try
      {
        var context = new MutationContext(db);
        await statusFlow.MoveAsync(context, entities, transition.Name, input, mutationCt);

        if (context.Errors.Count > 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = FormatErrors(context.Errors, entities)
          };
        }

        return new GridActionDto
        {
          Outcome = MutationOutcome.Committed,
          Warnings = context.Warnings.Count > 0 ? FormatErrors(context.Warnings, entities, MessageType.Warning) : null
        };
      }
      catch (DbUpdateConcurrencyException)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Conflict,
          Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
        };
      }
    }, ct: ct);
  }

  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    List<TEntity>? deletedRecords = null;

    var result = await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || !CanDelete())
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

      var hooks = GetHooks();
      if (!hooks.IsMultiDeleteAllowed && ids.Count > 1)
      {
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      var (entities, missing) = await LoadSelectedEntitiesAsync(ids, mutationCt, asTracking: true, lockEntities: true);
      if (missing.Count > 0)
      {
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = missing
        };
      }

      if (!CanDelete())
      {
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (HasVersionConflict(request, ids, entities))
      {
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Conflict,
          Deleted = 0,
          Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
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
          Warnings = FormatErrors(ctx.Errors, entities)
        };
      }

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
              Warnings = FormatErrors(ctx.Errors, entities)
            };
          }

          if (db.ChangeTracker.HasChanges())
          {
            await db.SaveChangesAsync(mutationCt);
          }
        }
      }
      catch (DbUpdateException)
      {
        return new GridDeleteDto
        {
          Outcome = MutationOutcome.Refused,
          Deleted = 0,
          Warnings = [new MessageDto { Detail = L["DeleteRefused"], Type = MessageType.Error }]
        };
      }

      deletedRecords = entities;
      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntitiesAsync(entities, db, mutationCt);
      }

      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Committed,
        Deleted = entities.Count,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, entities, MessageType.Warning) : null
      };
    }, ct: ct);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && deletedRecords is { Count: > 0 })
    {
      await PlatformApp.ClearAuthCacheForEntitiesAsync(deletedRecords, db, ct);
    }

    return result;
  }

  public virtual async Task<IReadOnlyList<MessageDto>> OnRestoredAsync(IReadOnlyList<object> restoredEntities, CancellationToken ct = default)
  {
    var hooks = GetHooks();
    if (hooks.AfterRestoreActions.Count == 0 || restoredEntities.Count == 0)
    {
      return [];
    }

    var typed = restoredEntities.OfType<TEntity>().ToList();
    if (typed.Count == 0)
    {
      return [];
    }

    var ids = typed.Select(e => e.Id).ToList();
    var ctx = new GridBatchContext<TEntity>(ids, typed, token => db.SaveChangesAsync(token), db);
    foreach (var h in hooks.AfterRestoreActions)
    {
      await h(ctx, ct);
    }

    return ctx.Errors.Count > 0 ? FormatErrors(ctx.Errors, typed, MessageType.Error) : [];
  }

  private static bool HasVersionConflict(GridRequest request, IReadOnlyList<long> ids, IReadOnlyList<TEntity> records)
  {
    if (request.SelectedVersions is not { Count: > 0 } versions)
    {
      return false;
    }

    if (versions.Count != ids.Count)
    {
      return true;
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

  private List<MessageDto> FormatErrors(IEnumerable<MutationError> errors, IReadOnlyList<TEntity> records, string defaultType = MessageType.Warning)
  {
    var queryDeclaration = GetQueryDeclaration();
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

  public virtual async Task<object?> ReadRowAsync(long id, CancellationToken ct = default)
  {
    var queryDeclaration = GetQueryDeclaration();
    var cols = CreateColumns();
    var query = RestrictedQuery().AsNoTracking().Where(e => e.Id == id);

    if (queryDeclaration.Selector != null)
    {
      query = IncludeColumnNavigations(query, cols.List);
      var row = await query.Select(queryDeclaration.Selector).FirstOrDefaultAsync(ct);
      if (row is null)
      {
        return null;
      }

      if (queryDeclaration.ComputeAction != null)
      {
        await queryDeclaration.ComputeAction([row], ct);
      }

      var shaped = ShapeRow(row, cols.List, queryDeclaration.RowFlags);
      await PopulateCoversAsync([shaped], cols.List, ct);
      await PopulateProjectedColorsAsync([shaped], cols.List, ct);
      await PopulateRowAvailabilityAsync([shaped], cols, ct);
      return shaped;
    }

    var defaultSelector = typeof(TRow) == typeof(TEntity)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(cols.List, HasCustomShapeRow)
      : null;
    var entity = defaultSelector != null
      ? await query.Select(defaultSelector).FirstOrDefaultAsync(ct)
      : await IncludeColumnNavigations(query, cols.List).FirstOrDefaultAsync(ct);
    if (entity is null)
    {
      return null;
    }

    var shapedEntity = ShapeRow((entity as TRow)!, cols.List, queryDeclaration.RowFlags);
    await PopulateCoversAsync([shapedEntity], cols.List, ct);
    await PopulateRowAvailabilityAsync([shapedEntity], cols, ct);
    return shapedEntity;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (request.Id is not > 0)
    {
      throw new KeyNotFoundException(Localizer.Instance["FormNotFound"]);
    }

    if (!await EnsureParentScopeAsync(lockParent: false, ct))
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    var cols = CreateColumns();

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase));
    var blockedByReadOnly = col?.EditableEvaluator != null
      ? Feature.IsReadOnly ||
        ResolveParentChildBuilder()?.IsReadOnly == true ||
        User != null && User.IsAuthenticated && !string.IsNullOrEmpty(Feature.Module) && !User.Can(Feature.Edit) && !User.Can(Feature.All) && !User.Can("*")
      : IsReadOnly;
    if (blockedByReadOnly || col == null || !col.IsEditable)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    if (col.EditableEvaluator != null)
    {
      object? evalTarget;
      if (typeof(TRow) == typeof(TEntity))
      {
        evalTarget = await IncludeColumnNavigations(RestrictedQuery(), [col]).FirstOrDefaultAsync(e => e.Id == request.Id.Value, ct)
          ?? throw new KeyNotFoundException(L["FormNotFound"]);
      }
      else
      {
        var baseQuery = IncludeColumnNavigations(BuildBaseEntityQuery(null, out _, out var qDecl, preserveParent: true), [col])
          .Where(e => e.Id == request.Id.Value);
        evalTarget = qDecl.Selector != null
          ? await baseQuery.AsNoTracking().Select(qDecl.Selector).FirstOrDefaultAsync(ct)
          : await baseQuery.FirstOrDefaultAsync(ct);
        if (evalTarget == null)
        {
          throw new KeyNotFoundException(L["FormNotFound"]);
        }
      }

      if (!col.EditableEvaluator(evalTarget))
      {
        return new GridCellDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Path = request.Path, Type = MessageType.Error }]
        };
      }
    }

    var formType = ResolveFormServiceType(cols);
    if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
    {
      var form = (IFormService)services.GetRequiredService(formType);
      if (PlatformApp != null)
      {
        form.AttachPlatformApp(PlatformApp);
      }

      var formReq = new FormRequest { Id = request.Id, Ver = request.Ver, ParentId = request.ParentId, ParentService = request.ParentService };
      return await form.SaveCellAsync(request.Id.Value, request.Path, request.Value, formReq, token => ReadRowAsync(request.Id.Value, token), ct);
    }

    return new GridCellDto
    {
      Outcome = MutationOutcome.Refused,
      Warnings = [new MessageDto { Detail = L["FormReadOnly"], Path = request.Path, Type = MessageType.Error }]
    };
  }

  public virtual async Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default)
  {
    var query = BuildFilteredEntityQuery(request, out _, out var queryDeclaration);
    var cols = CreateColumns();
    LocalizeAndConfigureColumns(cols.List);
    var exportColumns = cols.List.Where(c => !c.IsHidden && !string.IsNullOrWhiteSpace(c.Path) && !c.IsComputed && c.Cell is not AddOns.Covers.Contracts.GridCellCoverModel).ToList();

    if (request.SelectedIds is { Count: > 0 } selectedIds)
    {
      query = query.Where(e => selectedIds.Contains(e.Id));
    }

    if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
    {
      request.Sort = queryDeclaration.DefaultSort.ToList();
    }

    NormalizeGridRequest(request, request, queryDeclaration, isSelect: false, applyInitialFilters: false);

    const int limit = 100_000;
    IAsyncEnumerable<object> rowStream;
    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

    if (queryDeclaration.Selector != null)
    {
      var dataProvider = DataProvider.Create(IncludeColumnNavigations(query.AsNoTracking(), exportColumns), queryMap, queryDeclaration.Selector);
      rowStream = queryDeclaration.ComputeAction != null
        ? ShapeComputedStreamAsync(dataProvider.StreamAsync(request, limit + 1, ct), queryDeclaration.ComputeAction, cols.List, ct)
        : ShapeStreamAsync(dataProvider.StreamAsync(request, limit + 1, ct), cols.List, ct);
    }
    else
    {
      var exportSelector = typeof(TRow) == typeof(TEntity)
        ? GridProjectionBuilder.TryBuildSelector<TEntity>(exportColumns, HasCustomShapeRow)
        : null;
      var defaultDataProvider = exportSelector != null
        ? DataProvider.Create(query.AsNoTracking(), queryMap, exportSelector)
        : DataProvider.Create(IncludeColumnNavigations(query.AsNoTracking(), exportColumns), queryMap);
      rowStream = ShapeEntityStreamAsync(defaultDataProvider.StreamAsync(request, limit + 1, ct), cols.List, ct);
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    var label = L.TryGetString(Feature.Name, out var l) ? l : (string.IsNullOrEmpty(Feature.Name) ? typeof(TEntity).Name : Feature.Name);
    var cleanName = Regex.Replace(label, @"[\s\W]+", "_").Trim('_');
    var fileName = $"{cleanName}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
    TimeZones.TryFind(User?.TimeZone, out var exportTimeZone);
    await GridExcelWriter.WriteAsync(fileStream, label, exportColumns, col => PropertyAccessorCache.GetProperty(typeof(TRow), col.Path ?? "")?.PropertyType, rowStream, L, exportTimeZone, limit, L["ExportTruncated", limit], ct);
    fileStream.Position = 0;
    return Results.File(fileStream, GridExcelWriter.ContentType, fileName);
  }
}
