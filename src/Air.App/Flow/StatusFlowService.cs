namespace Air.App.Flow;

public abstract class StatusFlowService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : IRecordStatuses<TEntity>
  where TEntity : class, IEntity, new()
{
  protected PlatformDbContext Db => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());

  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  private readonly List<OptionDto> _statuses = [];
  private readonly List<Type> _followers = [];
  private readonly List<ChildRule> _childRules = [];
  private readonly Dictionary<string, Transition<TEntity>> _transitionsByName = new(StringComparer.OrdinalIgnoreCase);
  private Expression<Func<TEntity, long>>? _statusMember;
  private Action<TEntity, long>? _statusSetter;
  private bool _isDeclared;

  public virtual Feature Feature => new("", GetType().Name.Replace("FlowService", "").ToLowerInvariant(), GetType());

  protected Localizer L => Localizer.Instance.For(Feature.Module);

  public IReadOnlyList<Type> Followers
  {
    get
    {
      EnsureDeclared();
      return _followers;
    }
  }

  protected TFlowService FlowOf<TFlowService>() where TFlowService : class
  {
    if (PlatformApp?.Services.GetService(typeof(TFlowService)) is TFlowService resolved)
    {
      return resolved;
    }

    if (ModuleCatalog.Instance.CreateServiceInstance(typeof(TFlowService), Db, PlatformApp) is TFlowService created)
    {
      return created;
    }

    return (TFlowService)Activator.CreateInstance(typeof(TFlowService), Db)!;
  }

  public sealed class ChildRule(Func<IMutationContext, IReadOnlyList<TEntity>, CancellationToken, Task> executor)
  {
    private readonly List<Func<Move<TEntity>, CancellationToken, Task>> _effects = [];

    internal IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> Effects => _effects;

    public ChildRule Do(Func<Move<TEntity>, CancellationToken, Task> effect)
    {
      _effects.Add(effect);
      return this;
    }

    internal Task ExecuteAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken ct) =>
      executor(context, records, ct);
  }

  public sealed class ChildFlowRuleBuilder<TChild>(
    StatusFlowService<TEntity> parentFlow,
    StatusFlowService<TChild> childFlow,
    Expression<Func<TChild, long>> parentKey)
    where TChild : class, IEntity, new()
  {
    public ChildRule Give(
      Transition<TEntity> transition,
      Expression<Func<TChild, bool>>? any = null,
      Expression<Func<TChild, bool>>? rest = null)
    {
      ChildRule? rule = null;
      rule = new ChildRule(async (context, parents, ct) =>
      {
        if (parents.Count == 0)
        {
          return;
        }

        parentFlow.EnsureDeclared();
        childFlow.EnsureDeclared();
        var parentIds = parents.Select(p => p.Id).Distinct().ToList();
        var keyCompiled = parentKey.Compile();
        var param = parentKey.Parameters[0];
        var containsMethod = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
          .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
          .MakeGenericMethod(typeof(long));
        var filterExpr = Expression.Lambda<Func<TChild, bool>>(
          Expression.Call(containsMethod, Expression.Constant(parentIds), parentKey.Body),
          param);

        var allChildren = await parentFlow.Db.Set<TChild>()
          .Where(filterExpr)
          .ToListAsync(ct);

        var byParent = allChildren.ToLookup(keyCompiled);
        var anyPred = any?.Compile();
        var restPred = rest?.Compile();
        var toMove = new List<TEntity>();

        foreach (var parent in parents)
        {
          if (!parentFlow.Allows(parent, transition))
          {
            continue;
          }

          var children = byParent[parent.Id].ToList();
          if (children.Count == 0)
          {
            continue;
          }

          if (anyPred != null && !children.Any(anyPred))
          {
            continue;
          }

          if (restPred != null && !children.Where(c => anyPred == null || !anyPred(c)).All(restPred))
          {
            continue;
          }

          toMove.Add(parent);
        }

        if (toMove.Count > 0)
        {
          await parentFlow.ExecuteRuleTransitionAsync(context, toMove, transition, rule!.Effects, ct);
        }
      });
      return rule;
    }
  }

  protected ChildFlowRuleBuilder<TChild> Children<TChild>(
    StatusFlowService<TChild> childFlow,
    Expression<Func<TChild, long>> parentKey)
    where TChild : class, IEntity, new() =>
    new(this, childFlow, parentKey);

  public bool Allows(TEntity record, Transition<TEntity> transition)
  {
    EnsureDeclared();
    if (_statusMember == null)
    {
      return false;
    }

    var currentStatus = _statusMember.Compile()(record);
    return transition.From.Contains(currentStatus);
  }

  public Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    Transition<TEntity> transition,
    string? note = null,
    CancellationToken cancellationToken = default) =>
    MoveInternalAsync(context, records, transition, null, note, [], cancellationToken);

  public async Task FollowAsync(
    IMutationContext context,
    IEnumerable<TEntity> records,
    ChildRule rule,
    CancellationToken cancellationToken = default)
  {
    var list = records.Where(r => r.Id > 0).ToList();
    if (list.Count == 0)
    {
      return;
    }

    await rule.ExecuteAsync(context, list, cancellationToken);
  }

  public async Task FollowMovesAsync(
    IMutationContext context,
    IReadOnlySet<long> movedRecordIds,
    CancellationToken cancellationToken)
  {
    EnsureDeclared();
    if (movedRecordIds.Count == 0 || _childRules.Count == 0)
    {
      return;
    }

    var parents = await Db.Set<TEntity>()
      .Where(e => movedRecordIds.Contains(e.Id))
      .ToListAsync(cancellationToken);

    if (parents.Count == 0)
    {
      return;
    }

    foreach (var rule in _childRules)
    {
      await rule.ExecuteAsync(context, parents, cancellationToken);
      if (context.Errors.Count > 0)
      {
        return;
      }
    }
  }

  public Expression<Func<TEntity, long>> StatusMember
  {
    get
    {
      EnsureDeclared();
      return _statusMember!;
    }
  }

  public IReadOnlyList<OptionDto> Statuses
  {
    get
    {
      EnsureDeclared();
      return _statuses;
    }
  }

  public OptionDto? Initial
  {
    get
    {
      EnsureDeclared();
      return field;
    }

    private set;
  }

  private readonly List<Transition<TEntity>> _publicTransitions = [];

  public IReadOnlyList<RecordTransition> Transitions
  {
    get
    {
      EnsureDeclared();
      return _publicTransitions
        .OrderBy(t =>
        {
          var idx = _statuses.FindIndex(s => s.Id == t.To);
          return idx >= 0 ? idx : int.MaxValue;
        })
        .Select(t => new RecordTransition
        {
          Name = t.Name,
          From = t.From,
          To = t.To,
          Confirm = t.Question,
          Ask = t.Asked,
          Offers = t.Availability
        })
        .ToList();
    }
  }

  public string NameOf(long status) =>
    Statuses.FirstOrDefault(option => option.Id == status)?.Name ?? status.ToString();

  protected abstract void SetFlow(Flow<TEntity> flow);

  protected TransitionSource<TEntity> From(params long[] statuses)
  {
    EnsureDeclared();
    return new TransitionSource<TEntity>(statuses, t =>
    {
      _transitionsByName[t.Name] = t;
      return t;
    });
  }

  protected virtual Task OnMovedAsync(Move<TEntity> move, CancellationToken cancellationToken) => Task.CompletedTask;

  protected virtual ValueTask<string> RefusalAsync(Transition<TEntity> transition, long status, CancellationToken cancellationToken) =>
    new(L["StatusRefusesMove", L[NameOf(status)], L[NameOf(transition.To)]]);

  protected virtual Task LockAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken cancellationToken) =>
    Task.CompletedTask;

  public TEntity Initialize(TEntity record)
  {
    if (Initial is { } initial && _statusSetter != null)
    {
      _statusSetter(record, initial.Id);
    }

    return record;
  }

  public virtual async Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    string transitionName,
    object? input,
    CancellationToken cancellationToken
  )
  {
    EnsureDeclared();

    if (!_transitionsByName.TryGetValue(transitionName, out var transition))
    {
      throw new InvalidOperationException($"Transition '{transitionName}' is not declared in '{GetType().Name}'.");
    }

    await MoveInternalAsync(context, records, transition, input, transition.NoteOf(input), [], cancellationToken);
  }

  internal Task ExecuteRuleTransitionAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    Transition<TEntity> transition,
    IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> extraEffects,
    CancellationToken cancellationToken) =>
    MoveInternalAsync(context, records, transition, null, null, extraEffects, cancellationToken);

  private async Task MoveInternalAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    Transition<TEntity> transition,
    object? input,
    string? note,
    IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> extraEffects,
    CancellationToken cancellationToken)
  {
    EnsureDeclared();
    if (records.Count == 0)
    {
      return;
    }

    await LockAsync(context, records, cancellationToken);

    var statusReader = _statusMember!.Compile();
    foreach (var record in records)
    {
      var currentStatus = statusReader(record);
      if (!transition.From.Contains(currentStatus))
      {
        var refusal = await RefusalAsync(transition, currentStatus, cancellationToken);
        context.AddError("status.transition", refusal, record.Id);
        return;
      }

      foreach (var offer in transition.Availability)
      {
        if (offer.Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passes = await Db.Set<TEntity>().Where(e => e.Id == record.Id).AnyAsync(cond, cancellationToken);
          if (!passes)
          {
            context.AddError("status.availability", L[offer.Reason], record.Id);
            return;
          }
        }
      }
    }

    var userId = Db.TenantContext.UserId;

    foreach (var record in records)
    {
      var currentStatus = statusReader(record);
      var move = new Move<TEntity>
      {
        Record = record,
        From = currentStatus,
        To = transition.To,
        Transition = transition.Name,
        Note = note,
        UserId = userId,
        Context = context,
        Input = input
      };

      // Check guards
      foreach (var (passes, refusal, path) in transition.Guards)
      {
        if (!await passes(move, cancellationToken))
        {
          context.AddError("status.guard", refusal, record.Id, path);
          return;
        }
      }

      // Update entity status
      _statusSetter?.Invoke(record, transition.To);

      // Execute effects
      var errorsBefore = context.Errors.Count;
      foreach (var effect in transition.Effects)
      {
        await effect(move, cancellationToken);
        if (context.Errors.Count > errorsBefore)
        {
          _statusSetter?.Invoke(record, currentStatus);
          return;
        }
      }

      foreach (var extra in extraEffects)
      {
        await extra(move, cancellationToken);
        if (context.Errors.Count > errorsBefore)
        {
          _statusSetter?.Invoke(record, currentStatus);
          return;
        }
      }

      // Record move event (history)
      await OnMovedAsync(move, cancellationToken);
    }

    await context.FlushAsync(cancellationToken);

    if (_followers.Count > 0)
    {
      foreach (var followerType in _followers)
      {
        var follower = PlatformApp?.Services.GetService(followerType) as IRecordStatuses
          ?? ModuleCatalog.Instance.CreateServiceInstance(followerType, Db, PlatformApp) as IRecordStatuses;
        if (follower is StatusFlowService<TEntity> || follower == null)
        {
          continue;
        }

        // Collect parent key IDs for follower entity type
        var followerBase = followerType.BaseType;
        while (followerBase != null && (!followerBase.IsGenericType || followerBase.GetGenericTypeDefinition() != typeof(StatusFlowService<>)))
        {
          followerBase = followerBase.BaseType;
        }

        var parentEntityType = followerBase?.GetGenericArguments().FirstOrDefault();
        if (parentEntityType != null)
        {
          var fkProp = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType == typeof(long) &&
              (string.Equals(p.Name, parentEntityType.Name + "Id", StringComparison.OrdinalIgnoreCase) ||
               parentEntityType.Name.EndsWith(p.Name[..^2], StringComparison.OrdinalIgnoreCase)));
          if (fkProp != null)
          {
            var parentIds = records.Select(r => (long)fkProp.GetValue(r)!).Where(id => id > 0).ToHashSet();
            if (parentIds.Count > 0)
            {
              await follower.FollowMovesAsync(context, parentIds, cancellationToken);
              if (context.Errors.Count > 0)
              {
                return;
              }
            }
          }
        }
      }
    }
  }

  private void EnsureDeclared()
  {
    if (_isDeclared)
    {
      return;
    }

    _isDeclared = true;
    var flow = new Flow<TEntity>();
    SetFlow(flow);

    _statuses.AddRange(flow.DeclaredStatuses);
    _followers.AddRange(flow.FollowerTypes);
    if (flow.InitialStatusId.HasValue)
    {
      Initial = _statuses.FirstOrDefault(s => s.Id == flow.InitialStatusId.Value);
    }

    if (flow.StateMember != null)
    {
      _statusMember = flow.StateMember;
      if (_statusMember.Body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        _statusSetter = (entity, val) => pi.SetValue(entity, val);
      }
    }

    // Reflect transitions and child rules from properties
    var allProps = GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    foreach (var p in allProps.Where(p => p.PropertyType == typeof(Transition<TEntity>)))
    {
      if (p.GetValue(this) is Transition<TEntity> t)
      {
        _transitionsByName[t.Name] = t;
        if (p.GetMethod?.IsPublic == true)
        {
          _publicTransitions.Add(t);
        }
      }
    }

    foreach (var p in allProps.Where(p => p.PropertyType == typeof(ChildRule)))
    {
      if (p.GetValue(this) is ChildRule rule)
      {
        _childRules.Add(rule);
      }
    }
  }
}
