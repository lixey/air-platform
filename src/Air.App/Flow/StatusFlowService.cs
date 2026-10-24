namespace Platform.App.Flow;

public abstract class StatusFlowService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : IRecordStatuses<TEntity>
  where TEntity : class, IEntity, new()
{
  public sealed class Flow : Flow<TEntity>;

  public sealed class Move : Move<TEntity>;

  public sealed class Follow
  {
    public required TEntity Record { get; init; }
    public required IMutationContext Context { get; init; }
  }

  public sealed class Transition(string name, IReadOnlyList<long> from, long to) : Transition<TEntity>(name, from, to)
  {
    public new Transition When(Func<TEntity, bool> guard, string refusal, string? path = null)
    {
      base.When(guard, refusal, path);
      return this;
    }

    public new Transition When(Func<Move<TEntity>, CancellationToken, Task<bool>> guard, string refusal, string? path = null)
    {
      base.When(guard, refusal, path);
      return this;
    }

    public Transition When(Func<Move, CancellationToken, Task<bool>> guard, string refusal, string? path = null)
    {
      ArgumentNullException.ThrowIfNull(guard);
      base.When((m, ct) => guard((Move)m, ct), refusal, path);
      return this;
    }

    public new Transition AvailableWhen(Expression<Func<TEntity, bool>> condition, string reason)
    {
      base.AvailableWhen(condition, reason);
      return this;
    }

    public new Transition Do(Func<Move<TEntity>, CancellationToken, Task> effect)
    {
      base.Do(effect);
      return this;
    }

    public Transition Do(Func<Move, CancellationToken, Task> effect)
    {
      ArgumentNullException.ThrowIfNull(effect);
      base.Do((m, ct) => effect((Move)m, ct));
      return this;
    }

    public new Transition Ask<TInput>(Action<ActionInput<TInput>> elements, Func<TInput, string?>? note = null)
      where TInput : class, new()
    {
      base.Ask(elements, note);
      return this;
    }

    public new Transition Confirm(string question)
    {
      base.Confirm(question);
      return this;
    }
  }

  protected PlatformDbContext Db => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());

  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  private readonly List<OptionDto> _statuses = [];
  private readonly List<Type> _followers = [];
  private readonly List<ChildRule> _childRules = [];
  private readonly Dictionary<string, Transition<TEntity>> _transitionsByName = new(StringComparer.OrdinalIgnoreCase);
  private Expression<Func<TEntity, long>>? _statusMember;
  private Func<TEntity, long>? _statusReader;
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

  public class ChildRule(Func<IMutationContext, IReadOnlyList<TEntity>, CancellationToken, Task> executor)
  {
    private readonly List<Func<Move<TEntity>, CancellationToken, Task>> _moveEffects = [];
    private readonly List<Func<Follow, CancellationToken, Task>> _followEffects = [];

    internal IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> Effects => _moveEffects;
    internal IReadOnlyList<Func<Follow, CancellationToken, Task>> FollowEffects => _followEffects;

    public ChildRule Do(Func<Move<TEntity>, CancellationToken, Task> effect)
    {
      _moveEffects.Add(effect);
      return this;
    }

    public ChildRule Do(Func<Follow, CancellationToken, Task> effect)
    {
      _followEffects.Add(effect);
      return this;
    }

    internal virtual async Task ExecuteAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken ct)
    {
      await executor(context, records, ct);
      if (context.Errors.Count > 0 || _followEffects.Count == 0)
      {
        return;
      }

      foreach (var record in records)
      {
        var follow = new Follow { Record = record, Context = context };
        foreach (var effect in _followEffects)
        {
          await effect(follow, ct);
          if (context.Errors.Count > 0)
          {
            return;
          }
        }
      }

      await context.FlushAsync(ct);
    }
  }

  public sealed class ChildFlowRuleBuilder<TChild> : ChildRule
    where TChild : class, IEntity, new()
  {
    private readonly List<(
      Transition<TEntity> Transition,
      Expression<Func<TChild, bool>>? AnyPredicate,
      Expression<Func<TChild, bool>>? RestPredicate,
      IReadOnlyList<long>? AnyStatuses,
      IReadOnlyList<long>? RestStatuses
    )> _clauses = [];

    public ChildFlowRuleBuilder(
      StatusFlowService<TEntity> parentFlow,
      StatusFlowService<TChild> childFlow,
      Expression<Func<TChild, long>> parentKey)
      : base((context, parents, ct) => Task.CompletedTask)
    {
      _parentFlow = parentFlow;
      _childFlow = childFlow;
      _parentKey = parentKey;
    }

    private readonly StatusFlowService<TEntity> _parentFlow;
    private readonly StatusFlowService<TChild> _childFlow;
    private readonly Expression<Func<TChild, long>> _parentKey;

    public ChildFlowRuleBuilder<TChild> Give(
      Transition<TEntity> transition,
      Expression<Func<TChild, bool>>? any = null,
      Expression<Func<TChild, bool>>? rest = null)
    {
      _clauses.Add((transition, any, rest, null, null));
      return this;
    }

    public ChildFlowRuleBuilder<TChild> Give(
      Transition<TEntity> transition,
      IReadOnlyList<long> any,
      IReadOnlyList<long>? rest = null)
    {
      _clauses.Add((transition, null, null, any, rest));
      return this;
    }

    internal override async Task ExecuteAsync(IMutationContext context, IReadOnlyList<TEntity> parents, CancellationToken ct)
    {
      if (parents.Count == 0)
      {
        return;
      }

      _parentFlow.EnsureDeclared();
      _childFlow.EnsureDeclared();

      if (_clauses.Count > 0)
      {
        var parentIds = parents.Select(p => p.Id).Distinct().ToList();
        var keyCompiled = _parentKey.Compile();
        var param = _parentKey.Parameters[0];
        var containsMethod = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
          .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
          .MakeGenericMethod(typeof(long));
        var filterExpr = Expression.Lambda<Func<TChild, bool>>(
          Expression.Call(containsMethod, Expression.Constant(parentIds), _parentKey.Body),
          param);

        var allChildren = await _parentFlow.Db.Set<TChild>()
          .Where(filterExpr)
          .ToListAsync(ct);

        var byParent = allChildren.ToLookup(keyCompiled);
        var childStatusReader = _childFlow._statusReader ?? _childFlow.StatusMember.Compile();

        foreach (var parent in parents)
        {
          var children = byParent[parent.Id].ToList();
          if (children.Count == 0)
          {
            continue;
          }

          foreach (var clause in _clauses)
          {
            if (!_parentFlow.Allows(parent, clause.Transition))
            {
              continue;
            }

            if (clause.AnyStatuses != null)
            {
              var childStatuses = children.Select(childStatusReader).ToHashSet();
              if (!clause.AnyStatuses.Any(childStatuses.Contains))
              {
                continue;
              }

              var allowed = clause.RestStatuses != null
                ? clause.AnyStatuses.Concat(clause.RestStatuses).ToHashSet()
                : clause.AnyStatuses.ToHashSet();
              if (!childStatuses.All(allowed.Contains))
              {
                continue;
              }
            }
            else
            {
              var anyPred = clause.AnyPredicate?.Compile();
              var restPred = clause.RestPredicate?.Compile();
              if (anyPred != null && !children.Any(anyPred))
              {
                continue;
              }

              if (restPred != null && !children.Where(c => anyPred == null || !anyPred(c)).All(restPred))
              {
                continue;
              }
            }

            await _parentFlow.ExecuteRuleTransitionAsync(context, [parent], clause.Transition, Effects, ct);
            break;
          }
        }
      }

      if (context.Errors.Count > 0 || FollowEffects.Count == 0)
      {
        return;
      }

      foreach (var record in parents)
      {
        var follow = new Follow { Record = record, Context = context };
        foreach (var effect in FollowEffects)
        {
          await effect(follow, ct);
          if (context.Errors.Count > 0)
          {
            return;
          }
        }
      }

      await context.FlushAsync(ct);
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
    if (_statusReader == null)
    {
      return false;
    }

    var currentStatus = _statusReader(record);
    return transition.From.Contains(currentStatus);
  }

  public Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    Transition<TEntity> transition,
    string? note = null,
    CancellationToken cancellationToken = default) =>
    MoveInternalAsync(context, records, transition, null, note, [], cancellationToken);

  public Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    Transition<TEntity> transition,
    CancellationToken cancellationToken) =>
    MoveInternalAsync(context, records, transition, null, null, [], cancellationToken);

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

    await DispatchRuleAsync(rule, context, list, cancellationToken);
  }

  private static Task DispatchRuleAsync(
    ChildRule rule,
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    CancellationToken ct) =>
    rule.ExecuteAsync(context, records, ct);

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
      await DispatchRuleAsync(rule, context, parents, cancellationToken);
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

  protected virtual void SetFlow(Flow<TEntity> flow) { }
  protected virtual void SetFlow(Flow flow) => SetFlow((Flow<TEntity>)flow);

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
  protected virtual Task OnMovedAsync(Move move, CancellationToken cancellationToken) => OnMovedAsync((Move<TEntity>)move, cancellationToken);

  protected virtual ValueTask<string> RefusalAsync(Transition<TEntity> transition, long status, CancellationToken cancellationToken) =>
    new(L["StatusRefusesMove", L[NameOf(status)], L[NameOf(transition.To)]]);

  protected virtual ValueTask<string> RefusalAsync(Transition transition, long status, CancellationToken cancellationToken) =>
    RefusalAsync((Transition<TEntity>)transition, status, cancellationToken);

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

    var statusReader = _statusReader ??= _statusMember!.Compile();
    foreach (var record in records)
    {
      var currentStatus = statusReader(record);
      if (!transition.From.Contains(currentStatus))
      {
        var refusal = transition is Transition nestedTransition
          ? await RefusalAsync(nestedTransition, currentStatus, cancellationToken)
          : await RefusalAsync(transition, currentStatus, cancellationToken);
        context.AddError("status.transition", refusal, record.Id);
        return;
      }
    }

    if (transition.Availability.Count > 0)
    {
      var recordIds = records.Select(r => r.Id).ToList();
      foreach (var offer in transition.Availability)
      {
        if (offer.Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passedIds = (await Db.Set<TEntity>()
            .Where(e => recordIds.Contains(e.Id))
            .Where(cond)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

          foreach (var record in records)
          {
            if (!passedIds.Contains(record.Id))
            {
              context.AddError("status.availability", L[offer.Reason], record.Id);
              return;
            }
          }
        }
      }
    }

    var userId = Db.TenantContext.UserId;

    foreach (var record in records)
    {
      var currentStatus = statusReader(record);
      var move = new Move
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

      foreach (var (passes, refusal, path) in transition.Guards)
      {
        if (!await passes(move, cancellationToken))
        {
          context.AddError("status.guard", refusal, record.Id, path);
          return;
        }
      }

      _statusSetter?.Invoke(record, transition.To);

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
    var flow = new Flow();
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
      _statusReader = _statusMember.Compile();
      if (_statusMember.Body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        _statusSetter = (entity, val) => pi.SetValue(entity, val);
      }
    }

    var allProps = GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    foreach (var p in allProps.Where(p => typeof(Transition<TEntity>).IsAssignableFrom(p.PropertyType)))
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

    foreach (var p in allProps.Where(p => typeof(ChildRule).IsAssignableFrom(p.PropertyType)))
    {
      if (p.GetValue(this) is ChildRule rule)
      {
        _childRules.Add(rule);
      }
    }
  }
}
