using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
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
    ApplyElementConstraints(elements);

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
      if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }
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

    var input = entity is TInput same ? same : new TInput();
    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

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

    var hooks = new Hooks();
    SetHooks(hooks);

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (IsNew)
    {
      entity = new TEntity();
      if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }
      ApplyBoundParentId(entity, parent, Request.ParentId);
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
          [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]);
      }

      RestoreBoundParentId(entity, parent);
    }

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LocalizeAndResolveSelectElements(elements);
    _lastElements = elements;

    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["FormReadOnly"],
        [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]);
    }

    if (IsNew)
    {
      if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      var baselineInput = new TInput();
      CopyMatchingProperties(entity, baselineInput);
      var baselineLoadCtx = new FormLoadContext<TEntity, TInput>
      {
        Entity = entity,
        Input = baselineInput,
        IsNew = false
      };
      foreach (var h in hooks.AfterLoadActions)
      {
        await h(baselineLoadCtx, ct);
      }
      originalValues = SnapshotValues(baselineInput);
    }

    NormalizeNullableStrings(elements, input);
    var protectedPaths = BuildProtectedPaths(elements);
    CopyMatchingProperties(input, entity, protectedPaths);
    if (id.HasValue && id.Value > 0)
    {
      entity.Id = id.Value;
    }
    ApplyBoundParentId(entity, parent, Request.ParentId);
    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, input, originalValues, ct);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], ruleCtx.Failures);
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
    }

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }
    ValidateElementConstraints(elements, entity, input, ruleCtx, onlyAcceptingInput: false);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], ruleCtx.Failures);
    }

    await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

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

      if (saveCtx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
      }

      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    });

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }
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
    request.Id ??= id;
    Request = request;
    IsNew = false;

    var parent = new Parent();
    SetParent(parent);

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (request.Ver.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != request.Ver.Value)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Path = path, Type = MessageType.Error }]
      };
    }

    RestoreBoundParentId(entity, parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    ApplyElementConstraints(elements);

    if (IsReadOnly)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormReadOnly"], Path = path, Type = MessageType.Error }]
      };
    }

    var input = entity is TInput same ? same : new TInput();
    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

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

    var originalValues = SnapshotValues(input);

    SetPropertyValue(input, path, value);
    if (!ReferenceEquals(entity, input))
    {
      SetPropertyValue(entity, path, value);
    }
    NormalizeNullableStrings(elements, input);
    if (!ReferenceEquals(entity, input))
    {
      NormalizeNullableStrings(elements, entity);
    }

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
          Code = f.Code,
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

    var cellRefused = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      db.Set<TEntity>().Update(entity);
      await db.SaveChangesAsync(ct);

      foreach (var h in hooks.AfterSaveActions)
      {
        await h(saveCtx, ct);
      }

      if (saveCtx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return true;
      }

      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return false;
    });

    if (cellRefused)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = saveCtx.Errors
      };
    }

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

    var parent = new Parent();
    SetParent(parent);
    RestoreBoundParentId(entity, parent);

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

    await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      db.Set<TEntity>().Remove(entity);
      await db.SaveChangesAsync(ct);

      foreach (var h in hooks.AfterDeleteActions)
      {
        await h(deleteCtx, ct);
      }

      if (deleteCtx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], deleteCtx.Errors);
      }

      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    });
  }

  public virtual async Task<FormActionDto> MoveStatusAsync(
    long id,
    string transitionName,
    JsonElement? body,
    int? ver = null,
    CancellationToken ct = default)
  {
    Request = new FormRequest { Id = id, Ver = ver };
    IsNew = false;

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
    }

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new InvalidOperationException($"Record #{id} not found.");

    if (ver.HasValue && ver.Value > 0 && entity is IHasVersion versioned && versioned.Ver != ver.Value)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

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
    if (transition.Ask != null)
    {
      var askFailures = transition.Ask.Validate(input, L);
      if (askFailures.Count > 0)
      {
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [.. askFailures]
        };
      }
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var context = new MutationContext(db);
      await statusFlow.MoveAsync(context, [entity], transition.Name, input, ct);

      if (context.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
        };
      }

      await tx.CommitAsync(ct);
      return new FormActionDto
      {
        Outcome = MutationOutcome.Committed
      };
    });
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

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
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

    ValidateElementConstraints(elements, entity, input, ruleCtx, onlyAcceptingInput: true);

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    await ValidateUniqueElementsAsync(elements, entity, ruleCtx, ct);
    return (saveCtx, ruleCtx);
  }

  private void NormalizeNullableStrings(Elements elements, object target)
  {
    var targetType = target.GetType();
    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path)) continue;
      if (el.Constraints.AllowsNull == true && el.Constraints.RejectsBlank != true)
      {
        var prop = PropertyAccessorCache.GetProperty(targetType, el.Path);
        if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead && prop.CanWrite)
        {
          if (PropertyAccessorCache.GetValue(target, prop.Name) is string str && string.IsNullOrWhiteSpace(str))
          {
            prop.SetValue(target, null);
          }
        }
      }
    }
  }

  private void ValidateElementConstraints(
    Elements elements,
    TEntity entity,
    TInput input,
    RuleContext<TInput> ruleCtx,
    bool onlyAcceptingInput)
  {
    var efEntityType = db.Model.FindEntityType(typeof(TEntity));

    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path)) continue;

      var acceptsInput = !el.IsDisabled && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (onlyAcceptingInput)
      {
        if (!acceptsInput) continue;
      }
      else
      {
        if (acceptsInput || el.IsInputOnly || !el.IsRequired) continue;
      }

      var inputProp = PropertyAccessorCache.GetProperty(typeof(TInput), el.Path);
      var entityProp = !el.IsInputOnly ? PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path) : null;
      if (inputProp == null && entityProp == null) continue;

      var val = inputProp != null
        ? PropertyAccessorCache.GetValue(input, inputProp.Name)
        : PropertyAccessorCache.GetValue(entity, entityProp!.Name);

      var efProp = efEntityType != null && entityProp != null ? efEntityType.FindProperty(entityProp.Name) : null;
      var isFk = el.ValueKind == FormValueKind.Reference || !string.IsNullOrEmpty(el.ReferencePath) || (efProp?.IsForeignKey() ?? false);

      if (el.IsRequired && !AnswersRequirement(val, isFk))
      {
        if (!ruleCtx.Failures.Any(f => string.Equals(f.Path, el.Path, StringComparison.OrdinalIgnoreCase) && f.Code == "form.validation.required"))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["Required"],
            Code = "form.validation.required",
            Type = MessageType.Error
          });
        }
        continue;
      }

      if (!onlyAcceptingInput) continue;

      if (val is null)
      {
        if (el.Constraints.AllowsNull == false && !el.IsRequired)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationNull"],
            Code = "form.validation.null",
            Type = MessageType.Error
          });
        }
        continue;
      }

      if (val is string text)
      {
        if (text.Length > 0)
        {
          if (el.Constraints.MaxLength is { } maxLen && text.Length > maxLen)
          {
            ruleCtx.Failures.Add(new MessageDto
            {
              Path = el.Path,
              Detail = L["FormValidationMaxLength"],
              Code = "form.validation.maxLength",
              Type = MessageType.Error
            });
          }
          if (el.Constraints.MinLength is { } minLen && text.Length < minLen)
          {
            ruleCtx.Failures.Add(new MessageDto
            {
              Path = el.Path,
              Detail = L["FormValidationMinLength"],
              Code = "form.validation.minLength",
              Type = MessageType.Error
            });
          }

          if (el.Type == FormFieldType.Email && !IsValidEmail(text))
          {
            ruleCtx.Failures.Add(new MessageDto
            {
              Path = el.Path,
              Detail = L["FormValidationEmail"],
              Code = "form.validation.email",
              Type = MessageType.Error
            });
          }
          else if (el.Model is FormPhoneModel && !IsValidPhone(text))
          {
            ruleCtx.Failures.Add(new MessageDto
            {
              Path = el.Path,
              Detail = L["FormValidationPhone"],
              Code = "form.validation.phone",
              Type = MessageType.Error
            });
          }
          else if (el.Type == FormFieldType.Url && !IsValidSite(text))
          {
            ruleCtx.Failures.Add(new MessageDto
            {
              Path = el.Path,
              Detail = L["FormValidationUrl"],
              Code = "form.validation.url",
              Type = MessageType.Error
            });
          }
        }
      }

      if ((el.Constraints.Min.HasValue || el.Constraints.Max.HasValue) && TryConvertToDecimal(val, out var decVal))
      {
        var outOfRange = false;
        if (el.Constraints.Min is { ValueKind: JsonValueKind.Number } minEl && minEl.TryGetDecimal(out var minDec) && decVal < minDec)
        {
          outOfRange = true;
        }
        if (el.Constraints.Max is { ValueKind: JsonValueKind.Number } maxEl && maxEl.TryGetDecimal(out var maxDec) && decVal > maxDec)
        {
          outOfRange = true;
        }
        if (outOfRange)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationRange"],
            Code = "form.validation.range",
            Type = MessageType.Error
          });
        }
      }

      if (val is decimal decNumber && (el.Constraints.Precision.HasValue || el.Constraints.Scale.HasValue))
      {
        if (!FitsDecimal(decNumber, el.Constraints.Precision, el.Constraints.Scale))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationDecimal"],
            Code = "form.validation.decimal",
            Type = MessageType.Error
          });
        }
      }

      if (el.Model is FormSelectModel { Options: { } options } && val is long optId && optId > 0 && !options.Any(o => o.Id == optId))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = L["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormTimeZoneModel { Zones: { } zones } && val is string zone && !string.IsNullOrEmpty(zone) && !zones.Contains(zone, StringComparer.Ordinal))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = L["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormCheckboxSetModel { Options: { } checkOptions } && val is IEnumerable<long> checkIds)
      {
        var allowedIds = checkOptions.Select(o => o.Id).ToHashSet();
        if (checkIds.Any(id => !allowedIds.Contains(id)))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
      else if (el.Model is FormCheckboxTreeModel { Nodes: { } treeNodes } && val is IEnumerable<string> treeKeys)
      {
        var allowedKeys = Air.App.Auth.RightsTree.FlattenKeys(treeNodes);
        if (treeKeys.Any(k => !allowedKeys.Contains(k)))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
    }
  }

  private static bool AnswersRequirement(object? value, bool foreignKey) => value switch
  {
    null => false,
    string text => !string.IsNullOrWhiteSpace(text),
    ICollection collection => collection.Count > 0,
    Guid guid => guid != Guid.Empty,
    int key when foreignKey => key > 0,
    long key when foreignKey => key > 0,
    _ => true
  };

  private static bool TryConvertToDecimal(object value, out decimal result)
  {
    switch (value)
    {
      case decimal d: result = d; return true;
      case int i: result = i; return true;
      case long l: result = l; return true;
      case short s: result = s; return true;
      case byte b: result = b; return true;
      case double dbVal and not double.NaN and not double.PositiveInfinity and not double.NegativeInfinity:
        result = (decimal)dbVal; return true;
      case float flVal and not float.NaN and not float.PositiveInfinity and not float.NegativeInfinity:
        result = (decimal)flVal; return true;
      default:
        result = 0; return false;
    }
  }

  private static bool FitsDecimal(decimal value, int? precision, int? scale)
  {
    var bits = decimal.GetBits(value);
    var fractional = (bits[3] >> 16) & 0xff;
    var magnitude = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
    while (fractional > 0 && magnitude % 10 == 0)
    {
      magnitude /= 10;
      fractional--;
    }

    var digits = magnitude.IsZero ? 0 : magnitude.ToString(CultureInfo.InvariantCulture).Length;
    var integerDigits = Math.Max(0, digits - fractional);
    return (scale is null || fractional <= scale) &&
           (precision is null || (scale is { } s ? integerDigits <= precision - s : Math.Max(digits, fractional) <= precision));
  }

  private static bool IsValidEmail(string value)
  {
    var at = value.IndexOf('@');
    return at > 0 && at < value.Length - 1 && at == value.LastIndexOf('@');
  }

  private static bool IsValidPhone(string value) =>
    value is ['+', .. var digits] && digits.Length is >= 7 and <= 15 && digits.All(char.IsAsciiDigit);

  private static bool IsValidSite(string value) =>
    IsWebAddress(value, prefixed: false) || IsWebAddress("https://" + value, prefixed: true);

  private static bool IsWebAddress(string value, bool prefixed) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
    uri.Host.Contains('.', StringComparison.Ordinal) && !uri.Host.StartsWith('.') && !uri.Host.EndsWith('.') &&
    !(prefixed && uri.UserInfo.Length > 0);

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
