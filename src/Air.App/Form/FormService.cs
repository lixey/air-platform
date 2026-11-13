using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : IFormService
  where TEntity : class, IEntity, new()
{
  private static readonly ConcurrentDictionary<Type, bool> CustomRecalculateTypes = new();

  public PlatformDbContext DbContext => db;
  protected internal PlatformAppService? PlatformApp { get; private set; } = app;
  protected internal IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected internal ModuleCatalog Catalog => PlatformApp?.Catalog ?? ModuleCatalog.Instance;
  protected ICultureCatalog Cultures => PlatformApp?.Cultures ?? CultureCatalog.DefaultInstance;
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected TEntity? CurrentEntity { get; private set; }
  protected internal Localizer L => Localizer.Instance.For(Feature.Module);
  public abstract Feature Feature { get; }

  protected internal FormElements<TEntity> Elements { get; private set; } = new();
  protected internal FormQuery<TEntity> Query { get; private set; } = new();
  protected internal FormRules<TEntity> Rules { get; private set; } = new();
  protected internal FormHooks<TEntity> Hooks { get; private set; } = new();
  protected internal FormActions<TEntity> Actions { get; private set; } = new();
  protected internal FormNav Nav { get; private set; } = new();
  protected internal FormParent<TEntity> Parent { get; private set; } = new();

  protected virtual string Title =>
    !string.IsNullOrEmpty(Feature.Label)
      ? Feature.Label
      : (!string.IsNullOrEmpty(Feature.Name) ? Feature.Name : typeof(TEntity).Name);

  protected bool IsNew { get; set; }

  protected virtual bool IsReadOnly =>
    Feature.IsReadOnly ||
    ResolveParentChildBuilder()?.IsReadOnly == true ||
    User is not { IsAuthenticated: true } ||
    !ServiceEndpointsExtensions.CanEdit(Feature, User);

  protected FormRequest Request { get; set; } = new();
  protected long? ParentId => Request.ParentId;
  protected IDictionary<string, object?> ExecutionItems => State.Items;
  public string Service => ServicePathResolver.Resolve(GetType());
  public bool IsFormReadOnly => IsReadOnly;

  internal FormExecutionState<TEntity> State { get; } = new();

  public IReadOnlyList<MessageDto>? GetLastSaveWarnings() => State.LastSaveWarnings;

  protected static FormSaveContext<TEntity> SaveContextOf(RuleContext<TEntity> context) => context.SaveContext;

  protected virtual void SetParent() { }
  protected virtual void SetQuery() { }
  protected virtual void SetElements() { }

  protected virtual Task SetElementsAsync(CancellationToken cancellationToken = default)
  {
    SetElements();
    return Task.CompletedTask;
  }

  protected virtual void SetRules() { }
  protected virtual void SetHooks() { }
  protected virtual void SetActions() { }

  protected virtual Task SetActionsAsync(CancellationToken cancellationToken = default)
  {
    SetActions();
    return Task.CompletedTask;
  }

  protected virtual void SetNav() { }

  protected virtual Task SetNavAsync(CancellationToken cancellationToken = default)
  {
    SetNav();
    return Task.CompletedTask;
  }

  protected virtual OverlayBuilder? Presentation(string handle) => null;
  protected virtual Task RecalculateAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

  protected FormParent<TEntity> GetParent()
  {
    if (State.CachedParent != null)
    {
      return Parent = State.CachedParent;
    }

    Parent = new FormParent<TEntity>();
    SetParent();
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), Parent.BoundParentServiceType, "Parent.Bind/Read");
    return State.CachedParent = Parent;
  }

  protected FormHooks<TEntity> GetHooks()
  {
    if (State.CachedHooks != null)
    {
      return Hooks = State.CachedHooks;
    }

    Hooks = new FormHooks<TEntity>();
    SetHooks();
    return State.CachedHooks = Hooks;
  }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity =>
    ParentScopeValidator.GetParentRecord<TParent>(
      db,
      ParentId,
      Request.ParentService,
      State.PreloadedParent,
      State.PreloadedParentId,
      GetParent().BoundParentServiceType);

  protected TParent RequiredParent<TParent>() where TParent : class, IEntity =>
    ParentRecord<TParent>() ?? throw new ParentNotLoadedException(typeof(TParent), GetType());

  protected IQueryable<TEntity> Reachable(bool asTracking = false)
  {
    Query = new FormQuery<TEntity>();
    SetQuery();
    var query = Query.Source ?? db.Set<TEntity>();
    query = asTracking ? query.AsTracking() : query.AsNoTracking();
    foreach (var filter in Query.Filters)
    {
      query = query.Where(filter);
    }

    return query;
  }

  public void SetRequest(FormRequest request)
  {
    if (Request.Id != request.Id)
    {
      State.LastElements = null;
      State.ClearItems();
    }

    if (Request.ParentId != request.ParentId ||
        !string.Equals(Request.ParentService, request.ParentService, StringComparison.OrdinalIgnoreCase))
    {
      State.ParentRecordLoaded = false;
    }

    Request = request;
  }

  public async Task<object?> LoadParentRecordAsync(long id, CancellationToken ct = default)
  {
    if (id <= 0)
    {
      return null;
    }

    var loaded = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct);
    if (Request.Id is null || Request.Id == id)
    {
      CurrentEntity = loaded;
    }

    return loaded;
  }

  protected async Task EnsureParentScopeAsync(FormParent<TEntity> parent, bool lockParent, CancellationToken ct)
  {
    var (allowed, preloaded, parentChildren) = await ParentScopeValidator.ValidateAndLoadAsync(
      db,
      PlatformApp,
      Request.ParentId,
      Request.ParentService,
      parent.BoundProperty != null && !parent.IsOptional,
      parent.MatchesService,
      parent.BoundParentService,
      parent.BoundParentServiceType,
      parent.BoundParentEntityType,
      lockParent,
      ct);
    if (!allowed)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]);
    }

    State.CacheParentScope(
      preloaded,
      Request.ParentId,
      Request.ParentService,
      parent.BoundParentService,
      parentChildren,
      resetLoadedWhenMissing: true);
  }

  protected async Task PreloadParentRecordAsync(FormParent<TEntity> parent, CancellationToken ct)
  {
    var requireBoundParent = !IsNew && CurrentEntity != null && parent.BoundProperty != null && !parent.IsOptional;
    if (State.ParentRecordLoaded && State.PreloadedParentId == Request.ParentId && (!requireBoundParent || Request.ParentId is > 0))
    {
      return;
    }

    if (!Request.ParentId.HasValue && string.IsNullOrEmpty(Request.ParentService) && !requireBoundParent)
    {
      return;
    }

    var (allowed, preloaded, parentChildren) = await ParentScopeValidator.ValidateAndLoadAsync(
      db,
      PlatformApp,
      Request.ParentId,
      Request.ParentService,
      hasRequiredBoundParent: requireBoundParent,
      parent.MatchesService,
      parent.BoundParentService,
      parent.BoundParentServiceType,
      parent.BoundParentEntityType,
      lockParent: false,
      ct);
    if (!allowed)
    {
      throw new KeyNotFoundException(L["FormNotFound"]);
    }

    State.CacheParentScope(
      preloaded,
      Request.ParentId,
      Request.ParentService,
      parent.BoundParentService,
      parentChildren,
      resetLoadedWhenMissing: false);
  }

  public bool HasCustomRecalculate =>
    CustomRecalculateTypes.GetOrAdd(GetType(), static t =>
    {
      var m = t.GetMethod(
        nameof(RecalculateAsync),
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TEntity), typeof(CancellationToken)]);
      return m != null && m.DeclaringType != typeof(FormService<TEntity>);
    });

  public async Task<bool> NeedsParentRecalculationAsync(CancellationToken ct = default) =>
    HasCustomRecalculate || (await GetSumChildrenAsync(ct)).Count > 0;

  public async Task RecalculateParentAsync(IReadOnlyList<long> parentIds, CancellationToken ct = default)
  {
    if (parentIds.Count == 0)
    {
      return;
    }

    var sumChildren = await GetSumChildrenAsync(ct);
    if (!HasCustomRecalculate && sumChildren.Count == 0)
    {
      return;
    }

    if (db.Database.CurrentTransaction != null)
    {
      await MutationContext.AcquireLocksAsync(db, typeof(TEntity), parentIds.Select(id => new[] { id }).ToList(), ct);
    }

    foreach (var id in parentIds)
    {
      var entity = db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == id)
        ?? await db.Set<TEntity>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
      if (entity != null)
      {
        if (sumChildren.Count > 0)
        {
          await ChildSumEvaluator.ApplySumsAsync(db, PlatformApp, entity, sumChildren, ct);
        }

        if (HasCustomRecalculate)
        {
          await RecalculateAsync(entity, ct);
        }
      }
    }
  }

  private async Task EnsureEntityAndParentLoadedAsync(CancellationToken ct = default)
  {
    IsNew = Request.Id is null or <= 0;
    if (IsNew && CurrentEntity?.Id is > 0)
    {
      CurrentEntity = null;
    }

    var parent = GetParent();
    if (!IsNew && Request.Id is > 0 && CurrentEntity?.Id != Request.Id.Value)
    {
      CurrentEntity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == Request.Id.Value, ct);
      if (CurrentEntity != null)
      {
        RestoreBoundParentId(CurrentEntity, parent);
      }
    }

    if (Request.ParentId.HasValue || !string.IsNullOrEmpty(Request.ParentService) || !IsNew && CurrentEntity != null && parent.BoundProperty != null && !parent.IsOptional)
    {
      await PreloadParentRecordAsync(parent, ct);
    }
  }

  private void ApplyBoundParentId(TEntity entity, FormParent<TEntity> parent, long? parentId)
  {
    if (!parentId.HasValue ||
        parent.BoundProperty == null ||
        !parent.MatchesService(Request.ParentService) && ResolveParentChildBuilder(requirePreset: true)?.PresetProvider != null)
    {
      return;
    }

    if (parent.BoundProperty.PropertyType == typeof(long))
    {
      parent.BoundProperty.SetValue(entity, parentId.Value);
    }
    else if (parent.BoundProperty.PropertyType == typeof(long?))
    {
      parent.BoundProperty.SetValue(entity, (long?)parentId.Value);
    }
  }

  private void RestoreBoundParentId(TEntity entity, FormParent<TEntity> parent)
  {
    if (parent.BoundProperty == null ||
        !parent.MatchesService(Request.ParentService) && ResolveParentChildBuilder(requirePreset: true)?.PresetProvider != null)
    {
      return;
    }

    var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
    if (!IsNew && Request.ParentId is > 0 && storedParentId is > 0 && storedParentId.Value != Request.ParentId.Value)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]);
    }

    if (!IsNew && !parent.IsOptional || !Request.ParentId.HasValue)
    {
      if (storedParentId.HasValue)
      {
        Request.ParentId = storedParentId.Value;
      }
    }
  }

  private void ApplyParentChildPreset(TEntity entity, FormParent<TEntity> parent)
  {
    if (parent.BoundProperty != null && parent.MatchesService(Request.ParentService) ||
        Request.ParentId is not > 0 ||
        string.IsNullOrEmpty(Request.ParentService))
    {
      return;
    }

    var match = ResolveParentChildBuilder(requirePreset: true);
    if (match?.PresetProvider != null)
    {
      match.PresetProvider(db, Request.ParentId.Value, entity);
    }
  }

  private Task<ChildGridBuilder?> ResolveParentChildBuilderAsync(bool requirePreset = false, CancellationToken ct = default) =>
    ParentScopeValidator.ResolveChildBuilderAsync(
      db,
      PlatformApp,
      Request.ParentService,
      GetParent().BoundParentService,
      GetParent().BoundParentServiceType,
      Request.ParentId,
      val => State.CachedParentChildren = val,
      State.CachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false),
      ct);

  private ChildGridBuilder? ResolveParentChildBuilder(bool requirePreset = false) =>
    ParentScopeValidator.ResolveChildBuilder(
      Request.ParentService,
      GetParent().BoundParentService,
      Request.ParentId,
      State.CachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false));
}
