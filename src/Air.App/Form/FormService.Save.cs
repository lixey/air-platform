using System.Linq.Expressions;
using System.Text.Json;
using Air.App.Components;
using Air.App.Internal;
using Air.App.Mutations;
using Air.App.Screens;
using Air.Data.Contracts;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual async Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = (id is null or 0);

    var parent = new Parent();
    SetParent(parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
      ApplyBoundParentId(entity, parent, request.ParentId);
      if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      IQueryable<TEntity> query = db.Set<TEntity>();
      var efModel = db.Model.FindEntityType(typeof(TEntity));
      if (efModel != null)
      {
        foreach (var nav in efModel.GetNavigations())
        {
          if (!nav.IsCollection)
          {
            query = query.Include(nav.Name);
          }
        }
      }

      entity = await query.FirstOrDefaultAsync(e => e.Id == id!.Value, ct)
        ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

      RestoreBoundParentId(entity, parent);
    }

    var input = new TInput();
    CopyMatchingProperties(entity, input);

    var hooks = new Hooks();
    SetHooks(hooks);

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    return input;
  }

  public virtual Task<object> SaveAsync(JsonElement json, CancellationToken ct = default) =>
    SaveAsync(json, null, ct);

  public virtual async Task<object> SaveAsync(JsonElement json, FormRequest? request = null, CancellationToken ct = default)
  {
    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]);
    }

    var entityJson = json.ValueKind == JsonValueKind.Object && (json.TryGetProperty("value", out var val) || json.TryGetProperty("entity", out val))
      ? val
      : json;

    var input = JsonSerializer.Deserialize<TInput>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TInput();

    long? id = request?.Id;
    if ((id is null or <= 0) && input is IEntity inpEntity && inpEntity.Id > 0)
    {
      id = inpEntity.Id;
    }
    else if ((id is null or <= 0) && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp))
    {
      if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var rootId) && rootId > 0)
      {
        id = rootId;
      }
      else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var strId) && strId > 0)
      {
        id = strId;
      }
    }

    int? clientVer = request?.Ver;
    if (clientVer is null && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("ver", out var verProp) && verProp.ValueKind == JsonValueKind.Number && verProp.TryGetInt32(out var rootVer))
    {
      clientVer = rootVer;
    }
    else if (clientVer is null && input is IHasVersion verEntity && verEntity.Ver > 0)
    {
      clientVer = verEntity.Ver;
    }

    request ??= new FormRequest();
    request.Id = id;
    request.Ver = clientVer;
    if (!request.ParentId.HasValue && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var jsonPid))
    {
      request.ParentId = jsonPid;
    }
    if (string.IsNullOrEmpty(request.ParentService) && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } jsonPs)
    {
      request.ParentService = jsonPs;
    }

    Request = request;
    IsNew = (id is null or 0);

    var parent = new Parent();
    SetParent(parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    var hooks = new Hooks();
    SetHooks(hooks);

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (IsNew)
    {
      entity = new TEntity();
      ApplyBoundParentId(entity, parent, Request.ParentId);
      if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      entity = await db.Set<TEntity>().FindAsync([id!.Value], ct)
        ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

      if (clientVer.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != clientVer.Value)
      {
        throw new FormRefusedException(
          MutationOutcome.Conflict,
          L["MutationConflict"],
          [new MessageDto { Detail = L["FormConcurrentEdit"], Type = MessageType.Error }]);
      }

      RestoreBoundParentId(entity, parent);
      originalValues = SnapshotValues(entity);
    }

    var protectedPaths = BuildProtectedPaths(elements);
    CopyMatchingProperties(input, entity, protectedPaths);
    if (id.HasValue && id.Value > 0)
    {
      entity.Id = id.Value;
    }
    ApplyBoundParentId(entity, parent, Request.ParentId);

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, input, originalValues, ct);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormValidationFailed"], ruleCtx.Failures);
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], saveCtx.Errors);
    }

    if (IsNew)
    {
      db.Set<TEntity>().Add(entity);
    }
    else
    {
      db.Set<TEntity>().Update(entity);
    }
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterSaveActions)
    {
      await h(saveCtx, ct);
    }

    await db.SaveChangesAsync(ct);
    CopyMatchingProperties(entity, input);
    return input;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<object?>> readRow,
    CancellationToken ct = default)
  {
    if (IsReadOnly)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Path = path, Type = MessageType.Error }]
      };
    }

    Request = request;
    IsNew = false;

    var parent = new Parent();
    SetParent(parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (request.Ver.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != request.Ver.Value)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormConcurrentEdit"], Path = path, Type = MessageType.Error }]
      };
    }

    RestoreBoundParentId(entity, parent);
    var originalValues = SnapshotValues(entity);

    var input = new TInput();
    CopyMatchingProperties(entity, input);

    var hooks = new Hooks();
    SetHooks(hooks);

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = false
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    SetPropertyValue(input, path, value);
    SetPropertyValue(entity, path, value);

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, input, originalValues, ct);
    if (ruleCtx.Failures.Count > 0)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = ruleCtx.Failures.Select(f => new MessageDto
        {
          Detail = f.Detail,
          Path = f.Path ?? path,
          Type = MessageType.Error
        }).ToList()
      };
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = saveCtx.Errors
      };
    }

    db.Set<TEntity>().Update(entity);
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterSaveActions)
    {
      await h(saveCtx, ct);
    }
    await db.SaveChangesAsync(ct);

    var row = await readRow(ct);
    return new GridCellDto
    {
      Row = row,
      Outcome = MutationOutcome.Committed
    };
  }

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    if (User != null && User.IsAuthenticated && !User.Can(Feature.Delete) && !User.Can(Feature.All))
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]);
    }

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

    var hooks = new Hooks();
    SetHooks(hooks);

    var deleteCtx = new FormDeleteContext<TEntity> { Entity = entity };
    foreach (var h in hooks.BeforeDeleteActions)
    {
      await h(deleteCtx, ct);
    }

    if (deleteCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], deleteCtx.Errors);
    }

    db.Set<TEntity>().Remove(entity);
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterDeleteActions)
    {
      await h(deleteCtx, ct);
    }
    await db.SaveChangesAsync(ct);
  }

  public virtual async Task<FormActionDto> MoveStatusAsync(
    long id,
    string transitionName,
    JsonElement? body,
    CancellationToken ct = default)
  {
    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
    }

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new InvalidOperationException($"Record #{id} not found.");

    var transition = statusFlow.Transitions.FirstOrDefault(t =>
      string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException($"Transition '{transitionName}' not found in '{statusFlow.GetType().Name}'.");

    if (User != null && User.IsAuthenticated && !User.Can(Feature.Action(transition.Name)) && !User.Can(Feature.Edit) && !User.Can(Feature.All))
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    object? input = transition.Ask?.Read(body);

    var context = new MutationContext(db);
    await statusFlow.MoveAsync(context, [entity], transition.Name, input, ct);

    if (context.Errors.Count > 0)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
      };
    }

    return new FormActionDto
    {
      Outcome = MutationOutcome.Committed
    };
  }

  private async Task<(FormSaveContext<TEntity, TInput> SaveContext, RuleContext<TInput> RuleContext)> RunValidationPipelineAsync(
    Elements elements,
    Hooks hooks,
    TEntity entity,
    TInput input,
    IReadOnlyDictionary<string, object?>? originalValues,
    CancellationToken ct)
  {
    var saveCtx = new FormSaveContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew,
      OriginalValues = originalValues
    };

    foreach (var h in hooks.BeforeValidateActions)
    {
      h(saveCtx);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TInput>
    {
      Target = input,
      SaveContext = new FormSaveContext<object, TInput>
      {
        Entity = entity,
        Input = input,
        IsNew = IsNew,
        OriginalValues = originalValues
      }
    };

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    await ValidateUniqueElementsAsync(elements, entity, ruleCtx, ct);
    return (saveCtx, ruleCtx);
  }

  private async Task ValidateUniqueElementsAsync(Elements elements, TEntity entity, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    foreach (var el in elements.List)
    {
      if (el.UniqueWithin == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (prop == null || !prop.CanRead)
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(entity, prop.Name);
      if (val is null || (val is string str && string.IsNullOrWhiteSpace(str)))
      {
        continue;
      }

      var param = Expression.Parameter(typeof(TEntity), "e");
      Expression cond = Expression.Equal(Expression.Property(param, prop), Expression.Constant(val, prop.PropertyType));
      var skip = false;

      foreach (var withinName in el.UniqueWithin)
      {
        var withinProp = PropertyAccessorCache.GetProperty(typeof(TEntity), withinName);
        if (withinProp == null || !withinProp.CanRead)
        {
          skip = true;
          break;
        }

        var withinVal = PropertyAccessorCache.GetValue(entity, withinProp.Name);
        if (withinVal is null)
        {
          skip = true;
          break;
        }

        cond = Expression.AndAlso(cond, Expression.Equal(Expression.Property(param, withinProp), Expression.Constant(withinVal, withinProp.PropertyType)));
      }

      if (skip)
      {
        continue;
      }

      if (!IsNew && entity.Id > 0)
      {
        var idProp = PropertyAccessorCache.GetProperty(typeof(TEntity), nameof(IEntity.Id))!;
        cond = Expression.AndAlso(cond, Expression.NotEqual(Expression.Property(param, idProp), Expression.Constant(entity.Id, typeof(long))));
      }

      var exists = await db.Set<TEntity>().AsNoTracking().AnyAsync(Expression.Lambda<Func<TEntity, bool>>(cond, param), ct);
      if (exists)
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = L[el.UniqueMessage ?? "FormValidationUnique"],
          Code = "form.validation.unique",
          Type = MessageType.Error
        });
      }
    }
  }

  private static void ApplyBoundParentId(TEntity entity, Parent parent, long? parentId)
  {
    if (!parentId.HasValue || parent.BoundProperty == null) return;

    if (parent.BoundProperty.PropertyType == typeof(long))
      parent.BoundProperty.SetValue(entity, parentId.Value);
    else if (parent.BoundProperty.PropertyType == typeof(long?))
      parent.BoundProperty.SetValue(entity, (long?)parentId.Value);
  }

  private void RestoreBoundParentId(TEntity entity, Parent parent)
  {
    if (parent.BoundProperty == null || Request.ParentId.HasValue) return;
    var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
    if (storedParentId.HasValue)
    {
      Request.ParentId = storedParentId.Value;
    }
  }
}
