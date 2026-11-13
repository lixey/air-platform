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
  internal TEntity? Entity { get => CurrentEntity; set => CurrentEntity = value; }
  internal bool IsNewRecord { get => IsNew; set => IsNew = value; }
  internal FormRequest CurrentRequest { get => Request; set => Request = value; }

  public IReadOnlyList<MessageDto>? GetLastSaveWarnings() => State.LastSaveWarnings;
  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  protected static FormSaveContext<TEntity> SaveContextOf(RuleContext<TEntity> context) => context.SaveContext;

  protected virtual void SetParent(Parent parent) { }
  protected virtual void SetActions(Actions actions) { }

  protected virtual Task SetActionsAsync(Actions actions, CancellationToken ct = default)
  {
    SetActions(actions);
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
  protected virtual OverlayBuilder? Presentation(string handle) => null;
  protected virtual Task RecalculateAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

  protected Parent GetParent()
  {
    if (State.CachedParent != null)
    {
      return State.CachedParent;
    }

    var parent = new Parent();
    SetParent(parent);
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), parent.BoundParentServiceType, "Parent.Bind/Read");
    return State.CachedParent = parent;
  }

  protected Hooks GetHooks()
  {
    if (State.CachedHooks != null)
    {
      return State.CachedHooks;
    }

    var hooks = new Hooks();
    SetHooks(hooks);
    return State.CachedHooks = hooks;
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

    State.CacheParentScope(
      preloaded,
      Request.ParentId,
      Request.ParentService,
      parent.BoundParentService,
      parentChildren,
      resetLoadedWhenMissing: true);
  }

  protected async Task PreloadParentRecordAsync(Parent parent, CancellationToken ct)
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

  public virtual async Task<OverlayDto> OverlayAsync(string service, CancellationToken ct = default) =>
    FormOverlayResolver.BuildResolvedOverlay(this, service, await ProbeElementsForOverlayAsync(ct));

  public virtual OverlayDto Overlay(string service) =>
    FormOverlayResolver.BuildResolvedOverlay(this, service, State.LastElements ?? ProbeElementsSafeSync());

  protected virtual (string? Width, string? Height) OverlayAsk(Elements elements) =>
    FormOverlayResolver.ResolveOverlayAsk(this, elements);

  protected virtual bool FillsOverlay(Elements elements) =>
    FormOverlayResolver.FillsOverlay(this, elements);

  protected virtual string RecordCaption(TEntity? entity, long? id) =>
    FormOverlayResolver.FormatRecordCaption(this, entity, id, resolvedLabel: null);

  protected virtual Task<string> RecordCaptionAsync(TEntity? entity, long? id, CancellationToken ct = default) =>
    FormOverlayResolver.ResolveRecordCaptionAsync(this, entity, id, ct);

  public string GetTitle() => L[Title];
  public string GetRecordCaption(object? entity, long? id) => RecordCaption(entity as TEntity ?? State.LastSavedEntity, id);
  public Task<string> GetRecordCaptionAsync(object? entity, long? id, CancellationToken ct = default) =>
    RecordCaptionAsync(entity as TEntity ?? State.LastSavedEntity, id, ct);

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, new FormRequest { Id = id }, ct);

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default) =>
    FormScreenBuilder.BuildScreenAsync(this, servicePath, request, ct);

  public virtual Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default) =>
    FormScreenBuilder.EvaluateUnavailableAsync(this, id, ct);

  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default) =>
    FormModelLoader.LoadModelAsync(this, request, ct);

  public virtual Task<FormDataDto> BuildFormDataAsync(object? entity, ScreenDto? screen = null, string? outcome = null, CancellationToken ct = default) =>
    FormModelLoader.BuildFormDataAsync(this, entity, screen, outcome, ct);

  public virtual Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, CancellationToken ct = default) =>
    FormModelLoader.PopulateReferencesAsync(this, dict, entity, ct);

  public virtual Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, ScreenDto screen, CancellationToken ct = default) =>
    FormModelLoader.PopulateReferencesAsync(this, dict, entity, screen, ct);

  public virtual Task<object> SaveAsync(JsonElement json, CancellationToken ct = default) =>
    SaveAsync(json, null, ct);

  public virtual Task<object> SaveAsync(JsonElement json, FormRequest? request = null, CancellationToken ct = default) =>
    FormSavePipeline.SaveAsync(this, json, request, ct);

  public virtual Task<GridCellDto> SaveCellAsync(long id, string path, JsonElement value, FormRequest request, Func<CancellationToken, Task<RowDto?>> readRow, CancellationToken ct = default) =>
    FormSavePipeline.SaveCellAsync(this, id, path, value, request, readRow, ct);

  public virtual Task<FormDraftResultDto> PrepareDraftAsync(
    long? recordId = null,
    IReadOnlyDictionary<string, object?>? overrides = null,
    ISet<string>? pendingReferencePaths = null,
    long? parentId = null,
    string? parentService = null,
    string? triggerField = null,
    CancellationToken ct = default) =>
    FormModelLoader.PrepareDraftAsync(this, recordId, overrides, pendingReferencePaths, parentId, parentService, triggerField, ct);

  public virtual Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default) =>
    FormActionRunner.HasActionAsync(this, actionKey, ct);

  public virtual Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default) =>
    FormActionRunner.GetActionInputAsync(this, actionKey, request, ct);

  public virtual Task<FormActionDto?> ExecuteDeclaredActionAsync(string actionKey, FormRequest request, JsonElement? body, CancellationToken ct = default) =>
    FormActionRunner.ExecuteDeclaredActionAsync(this, actionKey, request, body, ct);

  public virtual Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, int? ver = null, CancellationToken ct = default) =>
    FormActionRunner.MoveStatusAsync(this, id, transitionName, body, ver, ct);

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    if (db != null)
    {
      await GetElementsMetadataAsync(ct);
    }
    else
    {
      var probe = await ProbeElementsSafeAsync(ct);
      foreach (var el in probe.List)
      {
        if (el.ReferenceServiceType != null)
        {
          ReferenceLookupAllowlist.RegisterReferrer(el.ReferenceServiceType, GetType());
        }
      }

      (probe.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    }

    (State.LastElements?.StatusFlow as IRecordStatuses)?.RegisterMetadata();

    foreach (var entry in (await ProbeActionsAsync(ct)).Entries)
    {
      entry.Ask?.ValidateConfiguration(GetType());
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

  IEnumerable<string> IPlatformService.GetDeclaredActions() =>
    ProbeActions().Entries
      .Select(e => e.Permission.GetDeclaredActionName(e.Handle))
      .Where(n => !string.IsNullOrWhiteSpace(n))!;

  IReadOnlyList<string> IFormService.GetRoutableActionMethods(ModuleCatalog catalog) =>
    FormMetadataValidator.GetRoutableActionMethods(this);

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog) =>
    FormMetadataValidator.ValidateConfiguration(this);

  void IFormService.ValidateInheritedGridActions(Type gridType) =>
    FormMetadataValidator.ValidateInheritedGridActions(this, gridType);

  Task<IReadOnlyList<(GridNavActionModel Model, bool IsInMenu, bool IsAllowedInReadOnly, IReadOnlyList<RecordAvailability> Offers)>> IFormService.BuildInheritedGridActionsAsync(
    string gridServicePath,
    long? parentId,
    string? parentService,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct) =>
    FormActionRunner.BuildInheritedGridActionsAsync(this, gridServicePath, parentId, parentService, isGridReadOnly, isParentReadOnly, ct);

  Task<IReadOnlyList<(string Key, IReadOnlyList<RecordAvailability> Offers)>> IFormService.GetActionOffersAsync(
    long? parentId,
    string? parentService,
    CancellationToken ct) =>
    FormActionRunner.GetActionOffersAsync(this, parentId, parentService, ct);

  Task<bool> IFormService.IsActionPermittedForGridAsync(
    string actionKey,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct) =>
    FormActionRunner.IsActionPermittedForGridAsync(this, actionKey, isGridReadOnly, isParentReadOnly, ct);

  internal OverlayBuilder? GetPresentation(string handle) => Presentation(handle);
  internal (string? Width, string? Height) GetOverlayAsk(Elements elements) => OverlayAsk(elements);
  internal bool DoesFillOverlay(Elements elements) => FillsOverlay(elements);
  internal string GetRawTitle() => Title;
  internal string InvokeRecordCaption(TEntity? entity, long? id) => RecordCaption(entity, id);
  internal Parent GetParentDeclaration() => GetParent();
  internal Hooks GetHooksDeclaration() => GetHooks();
  internal void InvokeSetElements(Elements elements) => SetElements(elements);
  internal Task InvokeSetElementsAsync(Elements elements, CancellationToken ct) => SetElementsAsync(elements, ct);
  internal void InvokeSetActions(Actions actions) => SetActions(actions);
  internal Task InvokeSetActionsAsync(Actions actions, CancellationToken ct) => SetActionsAsync(actions, ct);
  internal void InvokeSetRules(Rules rules) => SetRules(rules);
  internal IQueryable<TEntity> ReachableQuery(bool asTracking = false) => Reachable(asTracking);
  internal Task EnsureParentScopeInternalAsync(Parent parent, bool lockParent, CancellationToken ct) => EnsureParentScopeAsync(parent, lockParent, ct);
  internal Task PreloadParentRecordInternalAsync(Parent parent, CancellationToken ct) => PreloadParentRecordAsync(parent, ct);

  internal async Task EnsureEntityAndParentLoadedAsync(CancellationToken ct = default)
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

  internal async Task<Elements> EnsureElementsAsync(CancellationToken ct = default, bool reload = false)
  {
    if (!reload && State.LastElements != null)
    {
      return State.LastElements;
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
    State.LastElements = elements;
    return elements;
  }

  internal static void LockBoundParentElement(Elements elements, Parent parent, long? parentId)
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

  internal Elements ProbeElementsSafeSync()
  {
    var probe = new Elements();
    try
    {
      SetElementsAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch
    {
      if (probe.List.Count == 0 && probe.Children.Count == 0)
      {
        try
        {
          SetElements(probe);
        }
        catch
        {
        }
      }
    }

    return probe;
  }

  internal Actions ProbeActions()
  {
    var actions = new Actions();
    try
    {
      SetActionsAsync(actions).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          SetActions(actions);
        }
        catch (ParentNotLoadedException)
        {
        }
      }

      var overridesAsync = GetType()
        .GetMethod(nameof(SetActionsAsync), BindingFlags.Instance | BindingFlags.NonPublic)?
        .DeclaringType != typeof(FormService<TEntity>);
      if (db == null && overridesAsync && actions.Entries.Count == 0)
      {
        throw new InvalidOperationException(
          $"{GetType().Name}.{nameof(SetActionsAsync)} accessed null runtime dependencies during startup action discovery. Guard runtime-only queries with 'if (CurrentEntity != null)' or 'if (db != null)' before registering actions.",
          ex);
      }
    }

    return actions;
  }

  internal async Task<Actions> ProbeActionsAsync(CancellationToken ct)
  {
    var actions = new Actions();
    try
    {
      await SetActionsAsync(actions, ct);
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          SetActions(actions);
        }
        catch (ParentNotLoadedException)
        {
        }
      }
    }

    return actions;
  }

  internal void ApplyBoundParentId(TEntity entity, Parent parent, long? parentId)
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

  internal void RestoreBoundParentId(TEntity entity, Parent parent)
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

  internal void ApplyParentChildPreset(TEntity entity, Parent parent)
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

  internal Task<ChildGridBuilder?> ResolveParentChildBuilderAsync(bool requirePreset = false, CancellationToken ct = default) =>
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

  private async Task<IReadOnlyList<ChildGridBuilder>> GetSumChildrenAsync(CancellationToken ct)
  {
    if (State.LastElements != null)
    {
      return State.LastElements.Children.Where(c => !c.IsFiles && c.SumMappings.Count > 0).ToList();
    }

    var probe = await ProbeElementsSafeAsync(ct);
    return probe.Children.Where(c => !c.IsFiles && c.SumMappings.Count > 0).ToList();
  }

  private async Task<Elements> ProbeElementsSafeAsync(CancellationToken ct)
  {
    var probe = new Elements();
    try
    {
      await SetElementsAsync(probe, ct);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch
    {
      if (probe.List.Count == 0 && probe.Children.Count == 0)
      {
        try
        {
          SetElements(probe);
        }
        catch
        {
        }
      }
    }

    return probe;
  }

  private async Task<Elements> ProbeElementsForOverlayAsync(CancellationToken ct = default)
  {
    if (State.LastElements is not null)
    {
      return State.LastElements;
    }

    if (db != null)
    {
      await EnsureEntityAndParentLoadedAsync(ct);
    }

    return await ProbeElementsSafeAsync(ct);
  }
}
