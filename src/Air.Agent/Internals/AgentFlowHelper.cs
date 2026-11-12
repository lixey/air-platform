using System.Linq.Expressions;
using System.Reflection;

namespace Platform.Agent.Internals;

internal static class AgentFlowHelper
{
  private static readonly MethodInfo InspectGenericMethod = typeof(AgentFlowHelper)
    .GetMethod(nameof(InspectFlowTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo ValidateRecordGenericMethod = typeof(AgentFlowHelper)
    .GetMethod(nameof(ValidateRecordForTransitionTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo ExecuteDirectFlowGenericMethod = typeof(AgentFlowHelper)
    .GetMethod(nameof(ExecuteDirectFlowMoveTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

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

  public static bool CanMoveTransition(Feature feature, IUserContext user, string transitionName)
  {
    if (user is not { IsAuthenticated: true } || string.IsNullOrEmpty(feature.Module))
    {
      return false;
    }

    return feature.TryGetRight(transitionName, out var right) && user.Can(right) ||
           !feature.IsReadOnly && user.Can(feature.Edit) ||
           user.Can(feature.All) ||
           user.Can("*");
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

  public static async Task<(long CurrentStatusId, string CurrentStatusName, int? Ver)> ValidateRecordForTransitionAsync(
    Type flowServiceType,
    Feature feature,
    long recordId,
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
      .Invoke(null, [flowObj, feature, recordId, transition, db, app, ct])!;
    return await task;
  }

  public static async Task ExecuteMoveStatusAsync(
    Type? formServiceType,
    Type? gridServiceType,
    Type? flowServiceType,
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
        ModuleCatalog.Instance.CreateServiceInstance(formServiceType, db, app) is IFormService formInst)
    {
      try
      {
        result = await formInst.MoveStatusAsync(recordId, transitionName, askBody, ver, ct);
      }
      catch (InvalidOperationException ex) when (ex.Message.Contains("does not use a status flow", StringComparison.OrdinalIgnoreCase))
      {
        result = null;
      }
    }

    if (result == null &&
        gridServiceType != null &&
        ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, db, app) is IGridService gridInst)
    {
      try
      {
        result = await gridInst.MoveStatusAsync(
          new GridRequest { SelectedIds = [recordId], SelectedVersions = ver.HasValue ? [ver.Value] : null },
          transitionName,
          askBody,
          ct);
      }
      catch (InvalidOperationException ex) when (ex.Message.Contains("does not use a status flow", StringComparison.OrdinalIgnoreCase))
      {
        result = null;
      }
    }

    if (result == null && flowServiceType != null && FindFlowEntityType(flowServiceType) is { } entityType)
    {
      var flowObj = ModuleCatalog.Instance.CreateServiceInstance(flowServiceType, db, app)
        ?? throw new InvalidOperationException(Localizer.Instance["MutationRefused"]);
      var directTask = (Task<MutationResultDto>)ExecuteDirectFlowGenericMethod
        .MakeGenericMethod(entityType)
        .Invoke(null, [flowObj, recordId, transitionName, askBody, ver, db, app, ct])!;
      result = await directTask;
    }

    if (result == null)
    {
      throw new InvalidOperationException(Localizer.Instance["MutationRefused"]);
    }

    if (result.Outcome != MutationOutcome.Committed)
    {
      var first = result.Messages?.FirstOrDefault();
      var msg = first?.Detail ?? first?.Title ?? result.Title ?? Localizer.Instance["MutationRefused"];
      throw new InvalidOperationException(msg);
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
    flow.AttachPlatformApp(app);
    var localizer = Localizer.Instance.For(feature.Module);
    var statuses = flow.Statuses
      .Select(s => new OptionDto { Id = s.Id, Name = flow.NameOf(s.Id), Color = s.Color })
      .ToList();

    var rightByTransition = ReadRequiredRightsByTransitionName(flow);
    var list = new List<AgentFlowTransitionDescriptor>();
    if (!feature.AllowsAgentWrite)
    {
      return (statuses, list);
    }

    foreach (var t in flow.Transitions)
    {
      if (!CanMoveTransition(feature, app.User, t.Name))
      {
        continue;
      }

      var askElements = t.Ask != null
        ? await t.Ask.BuildResolvedElementsAsync(flowServiceType, feature, db, app, app.User, localizer, ct)
        : [];
      var toOpt = flow.Statuses.FirstOrDefault(s => s.Id == t.To);
      var fromNames = t.From.Select(flow.NameOf).ToList();
      rightByTransition.TryGetValue(t.Name, out var reqRightKey);
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

  private static Dictionary<string, string> ReadRequiredRightsByTransitionName<TEntity>(StatusFlowService<TEntity> flow)
    where TEntity : class, IEntity, new()
  {
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var dictField = typeof(StatusFlowService<TEntity>).GetField("_transitionsByName", BindingFlags.NonPublic | BindingFlags.Instance);
    if (dictField?.GetValue(flow) is System.Collections.IDictionary rawDict)
    {
      foreach (System.Collections.DictionaryEntry entry in rawDict)
      {
        if (entry.Key is string key && entry.Value is Transition<TEntity> { RequiredRight.Key: { Length: > 0 } rightKey })
        {
          dict[key] = rightKey;
        }
      }
    }

    return dict;
  }

  private static async Task<(long, string, int?)> ValidateRecordForTransitionTypedAsync<TEntity>(
    StatusFlowService<TEntity> flow,
    Feature feature,
    long recordId,
    AgentFlowTransitionDescriptor transition,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    flow.AttachPlatformApp(app);
    var localizer = Localizer.Instance.For(feature.Module);
    var entity = await db.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == recordId, ct)
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

  private static async Task<MutationResultDto> ExecuteDirectFlowMoveTypedAsync<TEntity>(
    StatusFlowService<TEntity> flow,
    long recordId,
    string transitionName,
    JsonElement? askBody,
    int? ver,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    flow.AttachPlatformApp(app);
    var localizer = Localizer.Instance.For(flow.Feature.Module);
    return await db.RunMutationAsync(async mutationCt =>
    {
      var entity = await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == recordId, mutationCt)
        ?? throw new KeyNotFoundException(localizer["FormNotFound"]);

      if (entity is IHasVersion versioned && ver is > 0 && versioned.Ver != ver.Value)
      {
        return new MutationResultDto
        {
          Outcome = MutationOutcome.Conflict,
          Messages = [new MessageDto { Detail = localizer["FormRecordChanged"], Type = MessageType.Error }]
        };
      }

      var transition = flow.Transitions.FirstOrDefault(t => string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));
      if (transition == null || !CanMoveTransition(flow.Feature, app.User, transition.Name))
      {
        return new MutationResultDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = localizer["MutationRefused"], Type = MessageType.Error }]
        };
      }

      var askInput = transition.Ask?.Read(askBody);
      if (transition.Ask != null)
      {
        var askErrors = await transition.Ask.ValidateAsync(askInput, db, app, localizer, mutationCt);
        if (askErrors.Count > 0)
        {
          return new MutationResultDto { Outcome = MutationOutcome.Refused, Messages = [.. askErrors] };
        }
      }

      var ctx = new MutationContext(db);
      await flow.MoveAsync(ctx, [entity], transition.Name, askInput, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        db.MarkMutationRollbackOnly();
        return new MutationResultDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
        };
      }

      await db.SaveChangesAsync(mutationCt);
      return new MutationResultDto { Outcome = MutationOutcome.Committed };
    }, ct);
  }
}
