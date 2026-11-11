using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : IFormService
  where TEntity : class, IEntity, new()
{
  private static readonly ConcurrentDictionary<Type, bool> CustomRecalculateTypes = new();

  private Elements? _lastElements;
  private Parent? _cachedParent;
  private Hooks? _cachedHooks;
  private TEntity? _lastSavedEntity;
  private List<MessageDto>? _lastSaveWarnings;
  private object? _preloadedParent;
  private long? _preloadedParentId;
  private bool _parentRecordLoaded;
  private (string? Service, long? ParentId, IReadOnlyList<ChildGridBuilder> Children)? _cachedParentChildren;

  public PlatformDbContext DbContext => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected TEntity? CurrentEntity { get; private set; }
  protected Localizer L => Localizer.Instance.For(Feature.Module);
  public abstract Feature Feature { get; }

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
  public string Service => ServicePathResolver.Resolve(GetType());

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
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), parent.BoundParentServiceType, "Parent.Bind/Read");
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

  public void SetRequest(FormRequest request)
  {
    if (Request.ParentId != request.ParentId ||
        !string.Equals(Request.ParentService, request.ParentService, StringComparison.OrdinalIgnoreCase))
    {
      _parentRecordLoaded = false;
    }

    Request = request;
  }

  public async Task<object?> LoadParentRecordAsync(long id, CancellationToken ct = default)
  {
    if (id <= 0)
    {
      return null;
    }

    var tracked = db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == id);
    var query = Reachable(asTracking: true);
    if (tracked != null)
    {
      var reachableExists = await query.AnyAsync(e => e.Id == id, ct);
      if (reachableExists)
      {
        if (Request.Id is null || Request.Id == id)
        {
          CurrentEntity = tracked;
        }

        return tracked;
      }

      return null;
    }

    var loaded = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (Request.Id is null || Request.Id == id)
    {
      CurrentEntity = loaded;
    }

    return loaded;
  }

  protected async Task EnsureParentScopeAsync(Parent parent, bool lockParent, CancellationToken ct)
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

    _preloadedParent = preloaded;
    _preloadedParentId = Request.ParentId;
    _parentRecordLoaded = Request.ParentId is > 0;
    var effectiveParentService = !string.IsNullOrEmpty(Request.ParentService)
      ? Request.ParentService
      : parent.BoundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && parentChildren != null)
    {
      _cachedParentChildren = (effectiveParentService, Request.ParentId, parentChildren);
    }
  }

  protected async Task PreloadParentRecordAsync(Parent parent, CancellationToken ct)
  {
    var requireBoundParent = !IsNew && CurrentEntity != null && parent.BoundProperty != null && !parent.IsOptional;
    if (_parentRecordLoaded && _preloadedParentId == Request.ParentId && (!requireBoundParent || Request.ParentId is > 0))
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

    _preloadedParent = preloaded;
    _preloadedParentId = Request.ParentId;
    if (Request.ParentId is > 0)
    {
      _parentRecordLoaded = true;
    }

    var effectiveParentService = !string.IsNullOrEmpty(Request.ParentService)
      ? Request.ParentService
      : parent.BoundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && parentChildren != null)
    {
      _cachedParentChildren = (effectiveParentService, Request.ParentId, parentChildren);
    }
  }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity =>
    ParentScopeValidator.GetParentRecord<TParent>(
      db,
      ParentId,
      Request.ParentService,
      _preloadedParent,
      _preloadedParentId,
      GetParent().BoundParentServiceType);

  public bool HasCustomRecalculate =>
    CustomRecalculateTypes.GetOrAdd(GetType(), static t =>
    {
      var m = t.GetMethod(
        nameof(RecalculateAsync),
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TEntity), typeof(CancellationToken)]);
      return m != null && m.DeclaringType != typeof(FormService<TEntity>);
    });

  protected virtual Task RecalculateAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

  public async Task RecalculateParentAsync(IReadOnlyList<long> parentIds, CancellationToken ct = default)
  {
    if (!HasCustomRecalculate || parentIds.Count == 0)
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
        await RecalculateAsync(entity, ct);
      }
    }
  }

  protected static FormSaveContext<TEntity> SaveContextOf(RuleContext<TEntity> context) => context.SaveContext;

  protected virtual void SetParent(Parent parent) { }
  protected virtual void SetNav(Nav nav) { }

  protected virtual Task SetNavAsync(Nav nav, CancellationToken ct = default)
  {
    SetNav(nav);
    return Task.CompletedTask;
  }

  protected virtual void SetQuery(Query query) { }
  protected virtual void SetElements(Elements elements) { }

  protected virtual Task SetElementsAsync(Elements elements, CancellationToken ct = default)
  {
    SetElements(elements);
    return Task.CompletedTask;
  }

  protected virtual void SetRules(Rules rules) { }
  protected virtual void SetHooks(Hooks hooks) { }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default)
  {
    SetRequest(request);
    await EnsureEntityAndParentLoadedAsync(ct);
    var nav = new Nav(Service, L);
    await SetNavAsync(nav, ct);
    var entry = nav.Actions.FirstOrDefault(a =>
      string.Equals(a.Model.Key, actionKey, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(a.Model.Method, actionKey, StringComparison.OrdinalIgnoreCase));
    return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(entry?.DefaultProvider, entry?.Ask, db, PlatformApp, ct);
  }

  protected Task<FormActionDto> ExecuteActionAsync(
    FormRequest request,
    Func<FormActionContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null) =>
    ExecuteActionAsync(request, (ctx, _, ct) => handler(ctx, ct), cancellationToken, actionName);

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

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    _ = GetParent();

    var probe = new Elements();
    SetElements(probe);
    FormConstraintExtractor.ValidateNavigationElements(typeof(TEntity), probe.List);

    if (probe.RecordLabelGridType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), probe.RecordLabelGridType, "UseRecordLabelOf");
    }

    if (probe.StatusFlow != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), probe.StatusFlow.GetType(), "UseStatusOf");
    }

    foreach (var child in probe.Children)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), child.ServiceType, $"AddChild '{child.DeclaredKey}'");
    }

    foreach (var el in probe.List)
    {
      if (el.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), el.ReferenceServiceType, $"AsReference on '{el.Path}'");
      }

      if (el.CreateFormServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), el.CreateFormServiceType, $"AllowCreate on '{el.Path}'");
      }
    }

    var nav = new Nav(Service, L);
    SetNav(nav);
    foreach (var entry in nav.Actions)
    {
      if (!ServiceEndpointsExtensions.IsCallableServiceAction(entry.Method.Method))
      {
        throw new InvalidOperationException(
          $"Form action method '{GetType().FullName}.{entry.Method.Method.Name}' registered via Nav.AddAction must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }
  }

  protected virtual OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  private async Task<bool> HasChildPagesAsync(CancellationToken ct = default)
  {
    if (_lastElements is not null)
    {
      return _lastElements.Children.Any(c => !c.IsFiles);
    }

    var probe = new Elements();
    try
    {
      await EnsureEntityAndParentLoadedAsync(ct);
      await SetElementsAsync(probe, ct);
    }
    catch
    {
    }

    return probe.Children.Any(c => !c.IsFiles);
  }

  public virtual async Task<OverlayDto> OverlayAsync(string service, CancellationToken ct = default) =>
    BuildResolvedOverlay(service, await HasChildPagesAsync(ct));

  public virtual OverlayDto Overlay(string service)
  {
    var hasChildPages = _lastElements?.Children.Any(c => !c.IsFiles);
    if (hasChildPages is null)
    {
      var probe = new Elements();
      try
      {
        SetElements(probe);
      }
      catch
      {
      }

      hasChildPages = probe.Children.Any(c => !c.IsFiles);
    }

    return BuildResolvedOverlay(service, hasChildPages == true);
  }

  private OverlayDto BuildResolvedOverlay(string service, bool hasChildPages)
  {
    var dto = Presentation($"{service}.modal").Screen(service).Build();
    if (dto.Presentation != OverlayPresentation.Drawer)
    {
      if (hasChildPages)
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
    if (declared.Presentation != OverlayPresentation.Dialog && declared.Presentation != OverlayPresentation.Popover ||
        declared.Width is not null ||
        declared.Height is not null)
    {
      return (null, null);
    }

    return elements.Children.Any(c => !c.IsFiles) ? (PanelSize.Fill, PanelSize.Fill) : (PanelSize.Px(720), null);
  }

  protected virtual bool FillsOverlay(Elements elements)
  {
    var declared = Overlay(Service);
    return declared.Presentation == OverlayPresentation.Drawer ||
           declared.Height == PanelSize.Fill ||
           OverlayAsk(elements).Height == PanelSize.Fill;
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
      catch
      {
      }
    }

    return string.IsNullOrWhiteSpace(label)
      ? L["FormRecord", title, id]
      : L["FormRecordNamed", title, label];
  }

  public string GetTitle() => L[Title];
  public string GetRecordCaption(object? entity, long? id) => RecordCaption(entity as TEntity ?? _lastSavedEntity, id);

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
      CurrentEntity = db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == Request.Id.Value)
        ?? await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == Request.Id.Value, ct);
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

  private async Task<Elements> EnsureElementsAsync(CancellationToken ct = default, bool reload = false)
  {
    if (!reload && _lastElements != null)
    {
      return _lastElements;
    }

    await EnsureEntityAndParentLoadedAsync(ct);
    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    var parent = GetParent();
    LockBoundParentElement(elements, parent, Request.ParentId);
    await FormReferenceResolver.LocalizeAndResolveSelectElementsAsync<TEntity>(
      GetType(),
      Feature,
      db,
      PlatformApp,
      User,
      L,
      IsReadOnly,
      elements,
      ct);
    _lastElements = elements;
    return elements;
  }

  private static void LockBoundParentElement(Elements elements, Parent parent, long? parentId)
  {
    if (parent.BoundProperty == null || parentId is not > 0)
    {
      return;
    }

    var boundPath = Elements.ToCamelPath(parent.BoundProperty.Name);
    foreach (var el in elements.List)
    {
      if (string.Equals(el.Path, boundPath, StringComparison.OrdinalIgnoreCase))
      {
        el.IsDisabled = true;
        el.IsServerOwned = true;
      }
    }
  }

  public async Task<(IReadOnlyList<FormElementDto> Elements, IReadOnlyList<ChildGridBuilder> Children)> GetElementsMetadataAsync(CancellationToken ct = default)
  {
    var elements = await EnsureElementsAsync(ct, reload: true);
    if (Feature.IsReadOnly)
    {
      foreach (var child in elements.Children)
      {
        child.ReadOnly();
      }
    }

    return (elements.List, elements.Children);
  }

  private void ApplyBoundParentId(TEntity entity, Parent parent, long? parentId)
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

  private void RestoreBoundParentId(TEntity entity, Parent parent)
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

  private void ApplyParentChildPreset(TEntity entity, Parent parent)
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
      Request.ParentId,
      val => _cachedParentChildren = val,
      _cachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false),
      ct);

  private ChildGridBuilder? ResolveParentChildBuilder(bool requirePreset = false) =>
    ParentScopeValidator.ResolveChildBuilder(
      Request.ParentService,
      GetParent().BoundParentService,
      Request.ParentId,
      _cachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false));
}
