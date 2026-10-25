namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected IQueryable<TEntity> RestrictedQuery()
  {
    var parent = GetParent();
    var queryDeclaration = GetQueryDeclaration();

    var query = BaseQuery(db);
    var restrictions = new UserRestrictions();
    SetUserRestrictions(restrictions);
    foreach (var restriction in restrictions.Filters)
    {
      query = query.Where(restriction);
    }

    if (ParentId.HasValue)
    {
      if (parent.FilterFunc != null && parent.MatchesService(ParentService))
      {
        query = parent.FilterFunc(query, ParentId.Value);
      }
      else if (ParentId is > 0 && ResolveParentChildBuilder() is { FilterProvider: { } filterProvider })
      {
        query = (IQueryable<TEntity>)filterProvider(db, ParentId.Value, query);
      }
      else if (parent.FilterFunc != null)
      {
        query = parent.FilterFunc(query, ParentId.Value);
      }
      else if (ParentId is > 0)
      {
        query = query.Where(_ => false);
      }
    }

    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

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
    CancellationToken ct)
  {
    if (ids.Count == 0)
    {
      return ([], []);
    }

    var rows = await RestrictedQuery().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
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

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: true, handler, cancellationToken);

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default)
    where TPicked : class, IEntity =>
    ExecutePickedAsync<TPicked>(
      request,
      isAttach: true,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: false, handler, cancellationToken);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default)
    where TPicked : class, IEntity =>
    ExecutePickedAsync<TPicked>(
      request,
      isAttach: false,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken);

  protected async Task<GridActionDto> AttachPickedAsync(
    GridPickRequest request,
    Action<TEntity, long> bindPicked,
    Func<TEntity> factory,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> afterAttach,
    CancellationToken cancellationToken = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (IsReadOnly)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    var parent = GetParent();

    var target = factory();
    if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
    {
      tenantEntity.TenantId = tid;
    }

    if (parent.BoundProperty != null && ParentId is > 0 && parent.BoundProperty.CanWrite)
    {
      parent.BoundProperty.SetValue(target, ParentId.Value);
    }

    bindPicked(target, request.PickedId);
    db.Set<TEntity>().Add(target);

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
      var ctx = new GridBatchContext<TEntity>([request.PickedId], [target], ct => db.SaveChangesAsync(ct), db);
      await afterAttach(ctx, cancellationToken);
      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(cancellationToken);
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(cancellationToken);
      }

      await tx.CommitAsync(cancellationToken);
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    });
  }

  protected async Task<GridActionDto> ReplacePickedAsync(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (IsReadOnly)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
    var target = targetId > 0
      ? await RestrictedQuery().AsTracking().FirstOrDefaultAsync(e => e.Id == targetId, cancellationToken)
      : null;
    if (target == null)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
      var ctx = new GridBatchContext<TEntity>([request.PickedId], [target], ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, cancellationToken);
      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(cancellationToken);
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(cancellationToken);
      }

      await tx.CommitAsync(cancellationToken);
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    });
  }

  private async Task<GridActionDto> ExecutePickedAsync<TPicked>(
    GridPickRequest request,
    bool isAttach,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken)
    where TPicked : class, IEntity
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (IsReadOnly)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    var picked = await db.Set<TPicked>().FirstOrDefaultAsync(e => e.Id == request.PickedId, cancellationToken);
    if (picked == null)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    var parent = GetParent();

    TEntity target;
    if (isAttach)
    {
      target = new TEntity();
      if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }

      if (parent.BoundProperty != null && ParentId is > 0 && parent.BoundProperty.CanWrite)
      {
        parent.BoundProperty.SetValue(target, ParentId.Value);
      }
    }
    else
    {
      var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
      var loaded = targetId > 0
        ? await RestrictedQuery().AsTracking().FirstOrDefaultAsync(e => e.Id == targetId, cancellationToken)
        : null;
      if (loaded == null)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
        };
      }

      target = loaded;
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
      var ctx = new GridBatchContext<TEntity>(target.Id > 0 ? [target.Id] : [], [target], ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, picked, target, cancellationToken);
      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(cancellationToken);
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (isAttach)
      {
        db.Set<TEntity>().Add(target);
      }
      else if (db.Entry(target).State == EntityState.Detached)
      {
        db.Set<TEntity>().Update(target);
      }

      await db.SaveChangesAsync(cancellationToken);
      await tx.CommitAsync(cancellationToken);
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    });
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
    var nav = new Nav(localizer: L);
    SetNav(nav);

    if (IsReadOnly && !nav.Actions.Any(a => a.IsAllowedInReadOnly))
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

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

    var (records, missing) = await LoadSelectedEntitiesAsync(ids, cancellationToken);
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
            .ToListAsync(cancellationToken))
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

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(cancellationToken);
      }

      await tx.CommitAsync(cancellationToken);
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, records, MessageType.Warning) : null
      };
    });
  }

  public virtual async Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    var cols = CreateColumns();
    if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused, Warnings = [new MessageDto { Detail = "No status flow configured for this grid.", Type = MessageType.Error }] };
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

    var (entities, missing) = await LoadSelectedEntitiesAsync(selectedIds, ct);
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
      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Warnings = context.Warnings.Count > 0 ? FormatErrors(context.Warnings, entities, MessageType.Warning) : null
      };
    });
  }

  public virtual async Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (!CanDelete())
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

    var (entities, missing) = await LoadSelectedEntitiesAsync(ids, ct);
    if (missing.Count > 0)
    {
      return new GridDeleteDto
      {
        Outcome = MutationOutcome.Refused,
        Deleted = 0,
        Warnings = missing
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

    var hooks = GetHooks();

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

        if (hooks.AfterDeleteActions.Count > 0)
        {
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

          if (db.ChangeTracker.HasChanges())
          {
            await db.SaveChangesAsync(ct);
          }
        }

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
        Deleted = entities.Count,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, entities, MessageType.Warning) : null
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

    var query = RestrictedQuery().Where(e => e.Id == id);
    query = IncludeColumnNavigations(query, cols.List);

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

      var shaped = ShapeRow(row, cols.List, queryDeclaration.RowFlags);
      await PopulateCoversAsync([shaped], cols.List, ct);
      await PopulateProjectedColorsAsync([shaped], cols.List, ct);
      await PopulateRowAvailabilityAsync([shaped], cols, ct);
      return shaped;
    }

    var entity = await query.FirstOrDefaultAsync(ct);
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

    var cols = CreateColumns();

    var col = cols.List.FirstOrDefault(c => string.Equals(c.Path, request.Path, StringComparison.OrdinalIgnoreCase));
    if (IsReadOnly || col == null || !col.IsEditable)
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
      if (PlatformApp != null)
      {
        form.AttachPlatformApp(PlatformApp);
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

    var entity = await RestrictedQuery().FirstOrDefaultAsync(e => e.Id == request.Id.Value, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (entity is IHasVersion verEntity && request.Ver.HasValue && verEntity.Ver != request.Ver.Value)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Path = request.Path, Type = MessageType.Error }]
      };
    }

    FormService<TEntity, TEntity>.SetPropertyValue(entity, request.Path, request.Value);
    if (db.Entry(entity).State == EntityState.Detached)
    {
      db.Set<TEntity>().Update(entity);
    }

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
    LocalizeAndConfigureColumns(cols.List);
    var exportColumns = cols.List.Where(c => !c.IsHidden && !string.IsNullOrWhiteSpace(c.Path) && !c.IsComputed && c.Cell is not AddOns.Covers.Contracts.GridCellCoverModel).ToList();

    if (request.SelectedIds is { Count: > 0 } selectedIds)
    {
      query = query.Where(e => selectedIds.Contains(e.Id));
    }

    query = IncludeColumnNavigations(query, exportColumns);

    if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
    {
      request.Sort = queryDeclaration.DefaultSort.ToList();
    }

    if (!string.IsNullOrWhiteSpace(request.Search) && queryDeclaration.PickKeySelector != null)
    {
      request.Search = queryDeclaration.PickKeySelector(request.Search);
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
      L["ExportTruncated", limit],
      ct
    );

    fileStream.Position = 0;
    return Results.File(fileStream, GridExcelWriter.ContentType, fileName);
  }
}
