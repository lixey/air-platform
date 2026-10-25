namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>(PlatformDbContext db, PlatformAppService? app = null) : IFormService
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  private Elements? _lastElements;
  private Parent? _cachedParent;
  private Hooks? _cachedHooks;
  private TEntity? _lastSavedEntity;
  private List<MessageDto>? _lastSaveWarnings;
  public IReadOnlyList<MessageDto>? GetLastSaveWarnings() => _lastSaveWarnings;
  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  protected Parent GetParent()
  {
    if (_cachedParent != null)
    {
      return _cachedParent;
    }

    var parent = new Parent();
    SetParent(parent);
    return _cachedParent = parent;
  }

  protected Hooks GetHooks()
  {
    if (_cachedHooks != null)
    {
      return _cachedHooks;
    }

    var hooks = new Hooks();
    SetHooks(hooks);
    return _cachedHooks = hooks;
  }

  public PlatformDbContext DbContext => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected TEntity? CurrentEntity { get; private set; }

  public sealed class Elements : Elements<TInput>
  {
    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Expression<Func<TChildEntity, long>> foreignKey,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
      builder.ChildEntityType = typeof(TChildEntity);
      builder.FilterProvider = (_, parentId, rawQuery) =>
      {
        Expression<Func<long>> idExpr = () => parentId;
        var param = foreignKey.Parameters[0];
        var body = Expression.Equal(foreignKey.Body, idExpr.Body);
        var predicate = Expression.Lambda<Func<TChildEntity, bool>>(body, param);
        return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
      };

      var expr = foreignKey.Body is UnaryExpression ue ? ue.Operand : foreignKey.Body;
      var directProp = expr is MemberExpression me && me.Expression == foreignKey.Parameters[0]
        ? me.Member as PropertyInfo
        : null;

      if (directProp is { CanWrite: true } || preset != null)
      {
        builder.PresetProvider = (ctxDb, parentId, childObj) =>
        {
          if (childObj is not TChildEntity child)
          {
            return;
          }

          if (directProp is { CanWrite: true })
          {
            directProp.SetValue(child, parentId);
          }

          if (preset != null)
          {
            var parent = LoadParentEntity(ctxDb, parentId);
            if (parent != null)
            {
              preset(child, parent);
            }
          }
        };
      }

      return builder;
    }

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Expression<Func<TChildEntity, long?>> foreignKey,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
      builder.ChildEntityType = typeof(TChildEntity);
      builder.FilterProvider = (_, parentId, rawQuery) =>
      {
        long? nullableParentId = parentId;
        Expression<Func<long?>> idExpr = () => nullableParentId;
        var param = foreignKey.Parameters[0];
        var body = Expression.Equal(foreignKey.Body, idExpr.Body);
        var predicate = Expression.Lambda<Func<TChildEntity, bool>>(body, param);
        return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
      };

      var expr = foreignKey.Body is UnaryExpression ue ? ue.Operand : foreignKey.Body;
      var directProp = expr is MemberExpression me && me.Expression == foreignKey.Parameters[0]
        ? me.Member as PropertyInfo
        : null;

      if (directProp is { CanWrite: true } || preset != null)
      {
        builder.PresetProvider = (ctxDb, parentId, childObj) =>
        {
          if (childObj is not TChildEntity child)
          {
            return;
          }

          if (directProp is { CanWrite: true })
          {
            directProp.SetValue(child, (long?)parentId);
          }

          if (preset != null)
          {
            var parent = LoadParentEntity(ctxDb, parentId);
            if (parent != null)
            {
              preset(child, parent);
            }
          }
        };
      }

      return builder;
    }

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Func<TEntity, Expression<Func<TChildEntity, bool>>> filter,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity =>
      AddChild<TChildGridService, TChildEntity>((_, parent) => filter(parent), preset);

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Func<PlatformDbContext, TEntity, Expression<Func<TChildEntity, bool>>> filter,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
      builder.ChildEntityType = typeof(TChildEntity);
      builder.FilterProvider = (ctxDb, parentId, rawQuery) =>
      {
        var parent = LoadParentEntity(ctxDb, parentId);
        if (parent == null)
        {
          return ((IQueryable<TChildEntity>)rawQuery).Where(_ => false);
        }

        var predicate = filter(ctxDb, parent);
        return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
      };

      if (preset != null)
      {
        builder.PresetProvider = (ctxDb, parentId, childObj) =>
        {
          if (childObj is not TChildEntity child)
          {
            return;
          }

          var parent = LoadParentEntity(ctxDb, parentId);
          if (parent != null)
          {
            preset(child, parent);
          }
        };
      }

      return builder;
    }

    private static TEntity? LoadParentEntity(PlatformDbContext ctxDb, long parentId)
    {
      var local = ctxDb.Set<TEntity>().Local.FirstOrDefault(e => e.Id == parentId);
      if (local != null)
      {
        return local;
      }

      return ctxDb.Set<TEntity>().AsNoTracking().FirstOrDefault(e => e.Id == parentId);
    }
  }

  protected Localizer L => Localizer.Instance.For(Feature.Module);

  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => !string.IsNullOrEmpty(Feature.Label) ? Feature.Label : (!string.IsNullOrEmpty(Feature.Name) ? Feature.Name : typeof(TEntity).Name);

  protected bool IsNew { get; set; }
  protected virtual bool IsReadOnly =>
    Feature.IsReadOnly ||
    ResolveParentChildBuilder()?.IsReadOnly == true ||
    User != null && User.IsAuthenticated && !User.Can(Feature.Edit) && !User.Can(Feature.All);
  protected FormRequest Request { get; set; } = new();
  protected long? ParentId => Request.ParentId;

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity
  {
    if (ParentId == null || ParentId.Value <= 0)
    {
      return null;
    }

    var tracked = db.Set<TParent>().Local.FirstOrDefault(e => e.Id == ParentId.Value);
    if (tracked != null)
    {
      return tracked;
    }

    var efModel = db.Model.FindEntityType(typeof(TParent));
    IQueryable<TParent> query = db.Set<TParent>();
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

    return query.FirstOrDefault(e => e.Id == ParentId.Value);
  }

  public sealed class Parent
  {
    public Type? BoundParentServiceType { get; private set; }
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool IsOptional { get; private set; }

    public bool MatchesService(string? parentService)
    {
      if (string.IsNullOrEmpty(parentService) || string.IsNullOrEmpty(BoundParentService))
      {
        return true;
      }

      if (string.Equals(parentService, BoundParentService, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      if (BoundParentServiceType != null &&
          ModuleCatalog.Instance.FindServiceType(parentService) is { } actualType)
      {
        if (BoundParentServiceType.IsAssignableFrom(actualType) ||
            actualType.IsAssignableFrom(BoundParentServiceType))
        {
          return true;
        }

        if (GetFormEntityType(BoundParentServiceType) is { } expectedEntity &&
            expectedEntity == GetFormEntityType(actualType))
        {
          return true;
        }
      }

      return false;
    }

    private static Type? GetFormEntityType(Type? type)
    {
      for (var t = type; t != null && t != typeof(object); t = t.BaseType)
      {
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(FormService<,>))
        {
          return t.GetGenericArguments()[0];
        }
      }

      return null;
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      var expr = member.Body;
      if (expr is UnaryExpression ue)
      {
        expr = ue.Operand;
      }

      if (expr is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      var expr = member.Body;
      if (expr is UnaryExpression ue)
      {
        expr = ue.Operand;
      }

      if (expr is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
    }
  }

  protected virtual void SetParent(Parent parent) { }

  public sealed class Hooks
  {
    internal List<Func<FormLoadContext<TEntity, TInput>, CancellationToken, Task>> AfterLoadActions { get; } = [];
    internal List<Action<FormSaveContext<TEntity, TInput>>> BeforeValidateActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> BeforeSaveActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> AfterSaveActions { get; } = [];
    internal List<Func<FormDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
    internal List<Func<FormDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

    public void AfterLoad(Func<FormLoadContext<TEntity, TInput>, CancellationToken, Task> handler) => AfterLoadActions.Add(handler);
    public void AfterLoad(Action<FormLoadContext<TEntity, TInput>> handler) =>
      AfterLoadActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void BeforeValidate(Action<FormSaveContext<TEntity, TInput>> handler) => BeforeValidateActions.Add(handler);

    public void BeforeSave(Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task> handler) => BeforeSaveActions.Add(handler);
    public void BeforeSave(Action<FormSaveContext<TEntity, TInput>> handler) =>
      BeforeSaveActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void AfterSave(Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task> handler) => AfterSaveActions.Add(handler);
    public void AfterSave(Action<FormSaveContext<TEntity, TInput>> handler) =>
      AfterSaveActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void BeforeDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<FormDeleteContext<TEntity>> handler) =>
      BeforeDeleteActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
    public void ValidateDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDelete(handler);
    public void ValidateDelete(Action<FormDeleteContext<TEntity>> handler) => BeforeDelete(handler);

    public void AfterDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);
    public void AfterDelete(Action<FormDeleteContext<TEntity>> handler) =>
      AfterDeleteActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
  }

  public sealed class Rules
  {
    internal List<Func<FormSaveContext<object, TInput>, RuleContext<TInput>, CancellationToken, Task>> Validators { get; } = [];

    public RuleBuilder<TInput, TProp> For<TProp>(Expression<Func<TInput, TProp>> member)
    {
      var compiled = member.Compile();
      string? path = null;
      var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
      if (expr is MemberExpression me)
      {
        path = char.ToLowerInvariant(me.Member.Name[0]) + me.Member.Name[1..];
      }

      var builder = new RuleBuilder<TInput, TProp>(ctx => compiled(ctx.Input), path);
      Validators.Add((ctx, rCtx, ct) => builder.ValidateAsync(ctx, rCtx, ct));
      return builder;
    }
  }

  protected static FormSaveContext<TEntity, TInput> SaveContextOf(RuleContext<TInput> context) =>
    new()
    {
      Entity = (TEntity)context.SaveContext.Entity,
      Input = context.SaveContext.Input,
      IsNew = context.SaveContext.IsNew,
      Db = context.SaveContext.Db,
      OriginalValues = context.SaveContext.OriginalValues
    };

  public sealed class FormNavActionEntry
  {
    public required FormNavActionModel Model { get; init; }
    public bool IsInMenu { get; set; }
    public bool IsAllowedInReadOnly { get; set; }
    public Delegate Method { get; init; } = null!;
    public Screens.RecordAsk? Ask { get; set; }
    public Func<CancellationToken, Task<object?>>? DefaultProvider { get; set; }
    public List<Screens.RecordAvailability> Offers { get; } = [];
  }

  public class FormNavActionBuilder(FormNavActionEntry entry, Nav nav)
  {
    public Nav InMenu()
    {
      entry.IsInMenu = true;
      return nav;
    }

    public FormNavActionBuilder AllowReadOnly(bool allow = true)
    {
      entry.IsAllowedInReadOnly = allow;
      return this;
    }
  }

  public sealed class FormNavActionBuilder<TRequest>(FormNavActionEntry entry, Nav nav) : FormNavActionBuilder(entry, nav)
    where TRequest : class, new()
  {
    public FormNavActionBuilder<TRequest> Ask(Action<Actions.ActionInput<TRequest>> elements)
    {
      entry.Ask = Actions.TransitionAsk.Of(elements);
      return this;
    }

    public FormNavActionBuilder<TRequest> Default(Func<CancellationToken, Task<TRequest>> provider)
    {
      entry.DefaultProvider = async ct => await provider(ct);
      return this;
    }

    public FormNavActionBuilder<TRequest> Default(Func<TRequest> provider)
    {
      entry.DefaultProvider = _ => Task.FromResult<object?>(provider());
      return this;
    }

    public FormNavActionBuilder<TRequest> Default(Func<TRequest, CancellationToken, Task> provider)
    {
      entry.DefaultProvider = async ct =>
      {
        var req = new TRequest();
        await provider(req, ct);
        return req;
      };
      return this;
    }

    public FormNavActionBuilder<TRequest> Default(Action<TRequest> provider)
    {
      entry.DefaultProvider = _ =>
      {
        var req = new TRequest();
        provider(req);
        return Task.FromResult<object?>(req);
      };
      return this;
    }

    public new FormNavActionBuilder<TRequest> AllowReadOnly(bool allow = true)
    {
      base.AllowReadOnly(allow);
      return this;
    }
  }

  public sealed class Nav(string servicePath = "", Localizer? localizer = null)
  {
    private readonly Localizer _l = localizer ?? Localizer.Instance;
    internal List<FormNavActionEntry> Actions { get; } = [];

    public FormNavActionBuilder AddAction(
      Delegate method,
      Action<Grid.GridService<TEntity, TEntity>.ButtonBuilder>? look = null,
      string? confirm = null)
    {
      var b = new Grid.GridService<TEntity, TEntity>.ButtonBuilder();
      look?.Invoke(b);
      var rawLabel = b.LabelText ?? method.Method.Name;
      var action = new FormNavActionModel
      {
        Service = servicePath,
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = _l[rawLabel],
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = _l[confirm], Color = b.ColorHandle ?? UiColor.Red } : null
      };
      var entry = new FormNavActionEntry
      {
        Model = action,
        IsInMenu = b.IsInMenu,
        Method = method
      };
      Actions.Add(entry);
      return new FormNavActionBuilder(entry, this);
    }

    public FormNavActionBuilder<TRequest> AddAction<TRequest>(
      Delegate method,
      Action<Grid.GridService<TEntity, TEntity>.ButtonBuilder>? look = null,
      string? confirm = null)
      where TRequest : class, new()
    {
      var b = new Grid.GridService<TEntity, TEntity>.ButtonBuilder();
      look?.Invoke(b);
      var rawLabel = b.LabelText ?? method.Method.Name;
      var action = new FormNavActionModel
      {
        Service = servicePath,
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = _l[rawLabel],
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = _l[confirm], Color = b.ColorHandle ?? UiColor.Red } : null
      };
      var entry = new FormNavActionEntry
      {
        Model = action,
        IsInMenu = b.IsInMenu,
        Method = method
      };
      Actions.Add(entry);
      return new FormNavActionBuilder<TRequest>(entry, this);
    }

    public void AvailableWhen(
      Delegate action,
      Expression<Func<TEntity, bool>> condition,
      string reason) =>
      AvailableWhen(condition, reason, action);

    public void AvailableWhen(
      Expression<Func<TEntity, bool>> condition,
      string reason,
      params Delegate[] actions)
    {
      var methodNames = actions.Select(a => a.Method.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var entry in Actions)
      {
        if (methodNames.Count == 0 || methodNames.Contains(entry.Method.Method.Name))
        {
          entry.Offers.Add(new Screens.RecordAvailability { Condition = condition, Reason = reason });
          entry.Model.UnavailableReasons ??= [];
          entry.Model.UnavailableReasons.Add(_l[reason]);
        }
      }
    }
  }

  protected virtual void SetNav(Nav nav) { }

  protected virtual Task SetNavAsync(Nav nav, CancellationToken ct = default)
  {
    SetNav(nav);
    return Task.CompletedTask;
  }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var nav = new Nav(Service, L);
    await SetNavAsync(nav, ct);
    var entry = nav.Actions.FirstOrDefault(a =>
      string.Equals(a.Model.Key, actionKey, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(a.Model.Method, actionKey, StringComparison.OrdinalIgnoreCase));
    if (entry?.DefaultProvider == null)
    {
      return new Dictionary<string, object?>();
    }

    var rawDefaults = await entry.DefaultProvider(ct);
    if (rawDefaults == null)
    {
      return new Dictionary<string, object?>();
    }

    var jsonText = JsonSerializer.Serialize(rawDefaults, PlatformJsonOptions.DefaultOptions);
    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
    return dict;
  }

  protected Task<FormActionDto> ExecuteActionAsync(
    FormRequest request,
    Func<FormActionContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null) =>
    ExecuteActionAsync(request, (ctx, _, ct) => handler(ctx, ct), cancellationToken, actionName);

  protected async Task<FormActionDto> ExecuteActionAsync(
    FormRequest request,
    Func<FormActionContext<TEntity>, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    Request = request;
    IsNew = false;
    if (request.Id is not > 0)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    var parent = GetParent();

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == request.Id.Value, cancellationToken)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (request.Ver.HasValue && request.Ver.Value > 0 && entity is IHasVersion versioned && versioned.Ver != request.Ver.Value)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

    RestoreBoundParentId(entity, parent);

    var nav = new Nav(Service, L);
    await SetNavAsync(nav, cancellationToken);
    var navEntry = !string.IsNullOrEmpty(actionName)
      ? nav.Actions.FirstOrDefault(a => string.Equals(a.Method.Method.Name, actionName, StringComparison.OrdinalIgnoreCase))
      : null;

    if (IsReadOnly && navEntry?.IsAllowedInReadOnly != true)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]
      };
    }

    if (navEntry is { Offers.Count: > 0 })
    {
      foreach (var offer in navEntry.Offers)
      {
        if (offer.Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passes = await db.Set<TEntity>().Where(e => e.Id == entity.Id).AnyAsync(cond, cancellationToken);
          if (!passes)
          {
            return new FormActionDto
            {
              Outcome = MutationOutcome.Refused,
              Warnings = [new MessageDto { Detail = L[offer.Reason], RecordId = entity.Id, Type = MessageType.Error }]
            };
          }
        }
      }
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
      var ctx = new FormActionContext<TEntity>(entity, ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, entity, cancellationToken);
      if (ctx.Errors.Count > 0)
      {
        await tx.RollbackAsync(cancellationToken);
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(cancellationToken);
      }

      await tx.CommitAsync(cancellationToken);
      if (PlatformApp != null)
      {
        await PlatformApp.EvictAuthCacheForEntityAsync(entity, db, cancellationToken);
      }

      return new FormActionDto
      {
        Outcome = MutationOutcome.Committed,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? ctx.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null
      };
    });
  }

  public sealed class Query
  {
    internal IQueryable<TEntity>? Source { get; private set; }
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];

    public Query From(IQueryable<TEntity> source)
    {
      Source = source;
      return this;
    }

    public Query Where(Expression<Func<TEntity, bool>> predicate)
    {
      Filters.Add(predicate);
      return this;
    }
  }

  protected virtual void SetQuery(Query query) { }

  protected IQueryable<TEntity> Reachable(bool asTracking = false)
  {
    var q = new Query();
    SetQuery(q);
    var query = q.Source ?? db.Set<TEntity>();
    query = asTracking ? query.AsTracking() : query.AsNoTracking();
    foreach (var filter in q.Filters)
    {
      query = query.Where(filter);
    }

    return query;
  }

  protected virtual void SetElements(Elements elements) { }

  protected virtual Task SetElementsAsync(Elements elements, CancellationToken ct = default)
  {
    SetElements(elements);
    return Task.CompletedTask;
  }

  protected virtual void SetRules(Rules rules) { }
  protected virtual void SetHooks(Hooks hooks) { }

  public string Service => ServicePathResolver.Resolve(GetType());

  protected virtual OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  private bool HasChildPages()
  {
    if (_lastElements is not null)
    {
      return _lastElements.Children.Any(c => !c.IsFiles);
    }

    var probe = new Elements();
    try
    {
      SetElementsAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch
    {
    }

    return probe.Children.Any(c => !c.IsFiles);
  }

  public virtual OverlayDto Overlay(string service)
  {
    var dto = Presentation($"{service}.modal").Screen(service).Build();
    if (dto.Presentation != OverlayPresentation.Drawer)
    {
      if (HasChildPages())
      {
        dto.Presentation = OverlayPresentation.Drawer;
        dto.Width = null;
        dto.Height = null;
      }
      else if (dto.Presentation == OverlayPresentation.Popover && dto.Width is null)
      {
        dto.Width = PanelSize.Px(720);
      }
    }

    return dto;
  }

  protected virtual (string? Width, string? Height) OverlayAsk(Elements elements)
  {
    var declared = Overlay(Service);
    if (declared.Presentation != OverlayPresentation.Dialog && declared.Presentation != OverlayPresentation.Popover
        || declared.Width is not null
        || declared.Height is not null)
    {
      return (null, null);
    }

    return elements.Children.Any(c => !c.IsFiles) ? (PanelSize.Fill, PanelSize.Fill) : (PanelSize.Px(720), null);
  }

  protected virtual bool FillsOverlay(Elements elements)
  {
    var declared = Overlay(Service);
    return declared.Presentation == OverlayPresentation.Drawer
           || declared.Height == PanelSize.Fill
           || OverlayAsk(elements).Height == PanelSize.Fill;
  }

  protected virtual string RecordCaption(TEntity? entity, long? id)
  {
    var title = L[Title];
    if (id is null or 0)
    {
      return L.TryGetString($"New {Title}", out var named) ? named : L["FormNewRecord", title];
    }

    string? label = null;
    if (_lastElements?.RecordLabelGridType is { } gridType && entity != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, db, PlatformApp) is Grid.IGridService gridInst)
        {
          label = gridInst.GetRecordLabel(entity);
        }
      }
      catch { }
    }

    return string.IsNullOrWhiteSpace(label)
      ? L["FormRecord", title, id]
      : L["FormRecordNamed", title, label];
  }

  public string GetTitle() => L[Title];

  public string GetRecordCaption(object? entity, long? id) => RecordCaption(entity as TEntity, id);

  public async Task<(IReadOnlyList<FormElementDto> Elements, IReadOnlyList<ChildGridBuilder> Children)> GetElementsMetadataAsync(CancellationToken ct = default)
  {
    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LocalizeAndResolveSelectElements(elements);
    if (Feature.IsReadOnly)
    {
      foreach (var child in elements.Children)
      {
        child.ReadOnly();
      }
    }

    _lastElements = elements;
    return (elements.List, elements.Children);
  }
}
