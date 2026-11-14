using System.Linq.Expressions;
using System.Reflection;

namespace Platform.Agent.Internals;

internal static class AgentFlowHelper
{
  private static readonly MethodInfo InspectGenericMethod = typeof(AgentFlowHelper)
    .GetMethod(nameof(InspectFlowTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo ValidateRecordGenericMethod = typeof(AgentFlowHelper)
    .GetMethod(nameof(ValidateRecordForTransitionTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  public static Type? FindFlowServiceType(Feature? feature)
  {
    if (feature == null)
    {
      return null;
    }

    foreach (var serviceType in ModuleCatalog.Instance.AllServiceTypes)
    {
      if (!serviceType.IsAbstract &&
          typeof(IRecordStatuses).IsAssignableFrom(serviceType) &&
          ModuleCatalog.Instance.FindFeature(serviceType) == feature)
      {
        return serviceType;
      }
    }

    return null;
  }

  public static bool CanMoveTransition(Feature feature, IUserContext user, string transitionName, Right? explicitRight = null) =>
    Feature.CanMove(feature, user, transitionName, explicitRight);

  public static Right? FindDeclaredTransitionRight(Type flowServiceType, string transitionName, PlatformDbContext db, PlatformAppService app)
  {
    if (ModuleCatalog.Instance.CreateServiceInstance(flowServiceType, db, app) is not IRecordStatuses flowObj)
    {
      return null;
    }

    return flowObj.AllTransitions
      .FirstOrDefault(t => string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase))
      ?.RequiredRight;
  }

  public static async Task<(IReadOnlyList<OptionDto> Statuses, IReadOnlyList<AgentFlowTransitionDescriptor> Transitions)> DiscoverFeatureFlowAsync(
    Feature? feature,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    var flowServiceType = FindFlowServiceType(feature);
    var entityType = FindFlowEntityType(flowServiceType);
    if (flowServiceType is not { } flowSvcType || entityType is not { } entType || feature is not { AllowsAgent: true })
    {
      return ([], []);
    }

    await using var scope = AgentModuleScope.OpenForService(rootServices, flowSvcType, fallbackDb, fallbackApp);
    if (ModuleCatalog.Instance.CreateServiceInstance(flowSvcType, scope.Db, scope.App) is not IRecordStatuses flowObj)
    {
      return ([], []);
    }

    var task = (Task<(IReadOnlyList<OptionDto>, IReadOnlyList<AgentFlowTransitionDescriptor>)>)InspectGenericMethod
      .MakeGenericMethod(entType)
      .Invoke(null, [flowObj, flowSvcType, feature, scope.Db, scope.App, ct])!;
    return await task;
  }

  public static async Task<(object? Entity, object? GridRow)> EnsureRecordReachableAsync(
    Feature feature,
    Type? formServiceType,
    Type? gridServiceType,
    long recordId,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    if (!AgentFeatureRegistry.CanViewFeature(feature, app) || formServiceType == null && gridServiceType == null)
    {
      throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
    }

    var localizer = Localizer.Instance.For(feature.Module);
    object? entity = null;
    if (formServiceType != null)
    {
      var formInst = ModuleCatalog.Instance.CreateServiceInstance(formServiceType, db, app) as IFormService
        ?? throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
      formInst.SetRequest(new FormRequest { Id = recordId });
      entity = await formInst.LoadParentRecordAsync(recordId, ct)
        ?? throw new KeyNotFoundException(localizer["FormNotFound"]);
    }

    object? gridRow = null;
    if (gridServiceType != null)
    {
      var gridInst = ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, db, app) as IGridService
        ?? throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
      gridRow = await gridInst.ReadRowAsync(recordId, ct)
        ?? throw new KeyNotFoundException(localizer["FormNotFound"]);
    }

    return (entity, gridRow);
  }

  public static async Task<(long CurrentStatusId, string CurrentStatusName, int? Ver)> ValidateRecordForTransitionAsync(
    Type flowServiceType,
    Feature feature,
    long recordId,
    object? reachableEntity,
    AgentFlowTransitionDescriptor transition,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var entityType = FindFlowEntityType(flowServiceType)
      ?? throw new InvalidOperationException(Localizer.Instance["MutationRefused"]);
    var flowObj = ModuleCatalog.Instance.CreateServiceInstance(flowServiceType, db, app)
      ?? throw new InvalidOperationException(Localizer.Instance["MutationRefused"]);

    var task = (Task<(long, string, int?)>)ValidateRecordGenericMethod
      .MakeGenericMethod(entityType)
      .Invoke(null, [flowObj, feature, recordId, reachableEntity, transition, db, app, ct])!;
    return await task;
  }

  public static async Task ExecuteMoveStatusAsync(
    Type? formServiceType,
    Type? gridServiceType,
    long recordId,
    string transitionName,
    JsonElement? askBody,
    int? ver,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    MutationResultDto? result = null;

    if (formServiceType != null &&
        ModuleCatalog.Instance.CreateServiceInstance(formServiceType, db, app) is IFormService formInst &&
        await formInst.HasStatusFlowAsync(ct))
    {
      result = await formInst.MoveStatusAsync(recordId, transitionName, askBody, ver, ct);
    }

    if (result == null &&
        gridServiceType != null &&
        ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, db, app) is IGridService gridInst &&
        gridInst.HasStatusFlow())
    {
      result = await gridInst.MoveStatusAsync(
        new GridRequest { SelectedIds = [recordId], SelectedVersions = ver.HasValue ? [ver.Value] : null },
        transitionName,
        askBody,
        ct);
    }

    if (result == null)
    {
      throw AgentRefusal.Create(Localizer.Instance["MutationRefused"]);
    }

    if (result.Outcome != MutationOutcome.Committed)
    {
      var first = result.Messages?.FirstOrDefault();
      var msg = first?.Detail ?? first?.Title ?? result.Title ?? Localizer.Instance["MutationRefused"];
      throw AgentRefusal.Create(msg);
    }
  }

  private static Type? FindFlowEntityType(Type? flowServiceType)
  {
    for (var cur = flowServiceType; cur != null && cur != typeof(object); cur = cur.BaseType)
    {
      if (cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(StatusFlowService<>))
      {
        return cur.GetGenericArguments()[0];
      }
    }

    return null;
  }

  private static async Task<(IReadOnlyList<OptionDto>, IReadOnlyList<AgentFlowTransitionDescriptor>)> InspectFlowTypedAsync<TEntity>(
    StatusFlowService<TEntity> flow,
    Type flowServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    ((IPlatformService)flow).BindContext(app);
    var localizer = Localizer.Instance.For(feature.Module);
    var statuses = flow.Statuses
      .Select(s => new OptionDto { Id = s.Id, Name = flow.NameOf(s.Id), Color = s.Color })
      .ToList();

    var list = new List<AgentFlowTransitionDescriptor>();
    if (feature.AgentAccess != AgentAccess.ReadWrite)
    {
      return (statuses, list);
    }

    foreach (var t in flow.AllTransitions)
    {
      if (!CanMoveTransition(feature, app.User, t.Name, t.RequiredRight))
      {
        continue;
      }

      var askElements = t.Ask != null
        ? await t.Ask.BuildResolvedElementsAsync(flowServiceType, feature, db, app, app.User, localizer, ct)
        : [];
      var toOpt = flow.Statuses.FirstOrDefault(s => s.Id == t.To);
      var fromNames = t.From.Select(flow.NameOf).ToList();
      var reqRightKey = t.RequiredRight?.Key;
      if (string.IsNullOrEmpty(reqRightKey) && feature.TryGetRight(t.Name, out var namedRight))
      {
        reqRightKey = namedRight.Key;
      }

      list.Add(new AgentFlowTransitionDescriptor(
        t.Name,
        flow.TransitionName(t.Name) ?? t.Name,
        !string.IsNullOrEmpty(t.Title) ? localizer[t.Title] : null,
        t.From,
        fromNames,
        t.To,
        flow.NameOf(t.To),
        toOpt?.Color,
        reqRightKey,
        t.Ask,
        askElements,
        t.Offers,
        flowServiceType));
    }

    return (statuses, list);
  }

  private static async Task<(long, string, int?)> ValidateRecordForTransitionTypedAsync<TEntity>(
    StatusFlowService<TEntity> flow,
    Feature feature,
    long recordId,
    object? reachableEntity,
    AgentFlowTransitionDescriptor transition,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    ((IPlatformService)flow).BindContext(app);
    var localizer = Localizer.Instance.For(feature.Module);
    var entity = reachableEntity as TEntity
      ?? await db.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == recordId, ct)
      ?? throw new KeyNotFoundException(localizer["FormNotFound"]);

    var currentStatusId = flow.StatusMember.Compile()(entity);
    var currentStatusName = flow.NameOf(currentStatusId);
    if (!transition.FromStatuses.Contains(currentStatusId))
    {
      throw new InvalidOperationException(localizer["StatusRefusesMove", currentStatusName, transition.ToStatusName]);
    }

    foreach (var offer in transition.Offers)
    {
      if (offer.Condition is Expression<Func<TEntity, bool>> cond)
      {
        var passes = await db.Set<TEntity>().Where(e => e.Id == recordId).AnyAsync(cond, ct);
        if (!passes)
        {
          throw new InvalidOperationException(localizer[offer.Reason]);
        }
      }
    }

    var ver = (entity as IHasVersion)?.Ver;
    return (currentStatusId, currentStatusName, ver);
  }
}
