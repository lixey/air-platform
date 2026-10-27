using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>(PlatformDbContext db, PlatformAppService? app = null) : IGridService
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  public PlatformDbContext DbContext => db;
  public PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected Localizer L => Localizer.Instance.For(Feature.Module);

  protected virtual void SetUserRestrictions(UserRestrictions restrictions) { }

  public virtual Feature Feature => new("", GetType().Name.Replace("GridService", "").ToLowerInvariant(), GetType());

  protected virtual bool IsReadOnly =>
    Feature.IsReadOnly ||
    ResolveParentChildBuilder()?.IsReadOnly == true ||
    User != null && User.IsAuthenticated && !string.IsNullOrEmpty(Feature.Module) && !User.Can(Feature.Edit) && !User.Can(Feature.All) && !User.Can("*");
  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  protected virtual void SetParent(Parent parent) { }
  protected internal string? ParentService { get; set; }
  protected internal long? ParentId { get; set; }
  protected IReadOnlyList<long> SelectedIds { get; set; } = [];

  private Parent? _cachedParent;
  private Hooks? _cachedHooks;

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

  public virtual bool CanView(GridRequest request, IUserContext? user) =>
    ServiceEndpointsExtensions.CanView(Feature, user);

  public virtual bool CanLookup(GridRequest request, IUserContext? user)
  {
    if (user is null or { IsAuthenticated: false })
    {
      return false;
    }

    if (CanView(request, user))
    {
      return true;
    }

    if (typeof(BaseAppFile).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthRole).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthGrant).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthAssignment).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthSession).IsAssignableFrom(typeof(TEntity)))
    {
      return false;
    }

    return ModuleCatalog.Instance.AllFeatures.Any(f =>
      f.ServiceType != GetType() && ServiceEndpointsExtensions.CanView(f, user));
  }

  protected IQueryable<TEntity> Reachable(bool asTracking = false)
  {
    var q = RestrictedQuery();
    return asTracking ? q.AsTracking() : q.AsNoTracking();
  }

  protected IQueryable<TEntity> Selected(bool asTracking = false) =>
    SelectedIds.Count > 0
      ? Reachable(asTracking).Where(e => SelectedIds.Contains(e.Id))
      : Reachable(asTracking).Where(_ => false);

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

  public virtual bool CanDelete()
  {
    if (IsReadOnly)
    {
      return false;
    }

    var deleteMethod = GetType().GetMethod(nameof(Delete), BindingFlags.Public | BindingFlags.Instance)
      ?? GetType().GetMethod(nameof(DeleteAsync), BindingFlags.Public | BindingFlags.Instance);

    if (deleteMethod != null)
    {
      if (deleteMethod.GetCustomAttribute<RequireRightAttribute>(inherit: true) is { } reqRight)
      {
        if (Feature.FeaturesByServiceType.TryGetValue(reqRight.Service, out var targetFeat))
        {
          return User == null || User.Can(targetFeat.Action(reqRight.ActionName)) || User.Can(targetFeat.All);
        }
      }

      if (deleteMethod.GetCustomAttribute<RequireActionAttribute>(inherit: true) is { } reqAction)
      {
        return User == null || User.Can(Feature.Action(reqAction.ActionName)) || User.Can(Feature.All);
      }
    }

    return Feature.HasDelete && (User == null || User.Can(Feature.Delete) || User.Can(Feature.All));
  }

  public virtual Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public virtual IReadOnlyList<string>? GetReferenceLabelPaths() =>
    GetQueryDeclaration().ReferenceLabelPaths;

  public virtual string? GetRecordLabel(object? entity)
  {
    if (entity is not TEntity typed)
    {
      return null;
    }

    var q = GetQueryDeclaration();
    if (q.LabelSelector != null)
    {
      try
      {
        return q.LabelSelector.Compile().DynamicInvoke(typed)?.ToString();
      }
      catch
      {
        return null;
      }
    }

    return null;
  }

  private static readonly ConcurrentDictionary<Type, bool> CustomShapeRowTypes = new();

  private bool HasCustomShapeRow =>
    CustomShapeRowTypes.GetOrAdd(GetType(), static t =>
    {
      var m1 = t.GetMethod("ShapeRow", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(TRow)]);
      var m3 = t.GetMethod(
        "ShapeRow",
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TRow), typeof(IReadOnlyList<GridColumnDto>), typeof(IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>)]);
      return m1 != null && m1.DeclaringType != typeof(GridService<TEntity, TRow>) ||
             m3 != null && m3.DeclaringType != typeof(GridService<TEntity, TRow>);
    });

  private (string? ParentService, long? ParentId, Query Value)? _cachedQuery;
  private (string? ParentService, long? ParentId, string ServicePath, Nav Value)? _cachedNav;
  private readonly Dictionary<string, OverlayDto> _cachedFormOverlays = new(StringComparer.OrdinalIgnoreCase);

  private Query GetQueryDeclaration()
  {
    if (_cachedQuery is { } cached && cached.ParentService == ParentService && cached.ParentId == ParentId)
    {
      return cached.Value;
    }

    var q = new Query();
    SetQuery(q);
    _cachedQuery = (ParentService, ParentId, q);
    return q;
  }

  private Nav GetNav(string? servicePath = null)
  {
    var effectivePath = servicePath ?? ServicePathResolver.Resolve(GetType());
    if (_cachedNav is { } cached &&
        cached.ParentService == ParentService &&
        cached.ParentId == ParentId &&
        string.Equals(cached.ServicePath, effectivePath, StringComparison.Ordinal))
    {
      return cached.Value;
    }

    var nav = new Nav(effectivePath, L);
    SetNav(nav);
    _cachedNav = (ParentService, ParentId, effectivePath, nav);
    return nav;
  }

  protected virtual void SetQuery(Query query) { }
  protected virtual void SetNav(Nav nav) { }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    SelectedIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    var nav = new Nav(ServicePathResolver.Resolve(GetType()), L);
    SetNav(nav);
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
    if (entry.Ask != null)
    {
      foreach (var el in entry.Ask.BuildElements())
      {
        if (string.IsNullOrEmpty(el.ReferencePath) || el.Model is not FormSelectModel selectModel || string.IsNullOrEmpty(selectModel.ReferenceService))
        {
          continue;
        }

        if (dict.TryGetValue(el.Path, out var rawVal) && rawVal is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var refId) && refId > 0)
        {
          var refServiceType = el.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService);
          if (ModuleCatalog.GetServiceEntityType(refServiceType) is { } refEntityType)
          {
            try
            {
              var targetDb = db.Model.FindEntityType(refEntityType) != null
                ? db
                : (ModuleCatalog.Instance.CreateServiceInstance(refServiceType!, db, PlatformApp) as IGridService)?.DbContext ?? db;
              var loaded = await targetDb.FindAsync(refEntityType, [refId], ct);
              var name = loaded != null
                ? (PropertyAccessorCache.GetValue(loaded, "Name") ?? PropertyAccessorCache.GetValue(loaded, "Title") ?? PropertyAccessorCache.GetValue(loaded, "Number"))?.ToString()
                : null;
              dict[el.ReferencePath] = new Reference { Id = refId, Name = !string.IsNullOrEmpty(name) ? name : $"#{refId}" };
            }
            catch { }
          }
        }
      }
    }

    return dict;
  }

  protected virtual void SetHooks(Hooks hooks) { }
  protected virtual void SetColumns(Columns columns) { }
  protected virtual void SetColumns(Columns<TRow> columns) { }

  private Columns CreateColumns()
  {
    var cols = new Columns();
    SetColumns(cols);
    SetColumns((Columns<TRow>)cols);
    return cols;
  }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();

  private Type? ResolveFormServiceType(Columns columns)
  {
    if (columns.FormServiceType != null)
    {
      return columns.FormServiceType;
    }

    if (FormServicePath != null)
    {
      return ModuleCatalog.Instance.FindServiceType(FormServicePath);
    }

    var candidateFormName = GetType().Name.Replace("GridService", "FormService");
    return GetType().Assembly.GetType(GetType().Namespace + "." + candidateFormName)
      ?? GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateFormName);
  }

  private string? ResolveFormServicePath(Columns columns)
  {
    if (FormServicePath != null)
    {
      return FormServicePath;
    }

    var formType = ResolveFormServiceType(columns);
    return formType != null ? ServicePathResolver.Resolve(formType) : null;
  }

  public virtual IReadOnlyList<GridCreateDescriptor> GetCreateForms()
  {
    if (IsReadOnly)
    {
      return [];
    }

    var servicePath = ServicePathResolver.Resolve(GetType());
    var nav = GetNav(servicePath);
    if (nav.Creates.Count > 0)
    {
      return nav.Creates
        .Select(c => new GridCreateDescriptor(
          c.FormType,
          c.Model.Service ?? ServicePathResolver.Resolve(c.FormType),
          c.Model.Label ?? c.Model.Title ?? L["Add"],
          c.Model.Color))
        .ToList();
    }

    var columns = CreateColumns();
    var formType = ResolveFormServiceType(columns);
    var formPath = ResolveFormServicePath(columns);
    if (formType != null && !string.IsNullOrEmpty(formPath))
    {
      return [new GridCreateDescriptor(formType, formPath, L[Feature.Label], UiColor.Orange)];
    }

    return [];
  }
}
