using Platform.App.Internals;

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

    public new Transition RequireRight(Right right)
    {
      base.RequireRight(right);
      return this;
    }

    public new Transition Label(string label)
    {
      base.Label(label);
      return this;
    }

    public new Transition Title(string title)
    {
      base.Title(title);
      return this;
    }

    public new Transition Title(string uiSource, string dbSource)
    {
      base.Title(uiSource, dbSource);
      return this;
    }
  }

  protected PlatformDbContext Db => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;

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
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), typeof(TFlowService), nameof(FlowOf));
    return (TFlowService)(PlatformApp?.Services.GetService(typeof(TFlowService))
      ?? ModuleCatalog.Instance.CreateServiceInstance(typeof(TFlowService), Db, PlatformApp)
      ?? throw new InvalidOperationException($"Cannot resolve or instantiate flow service '{typeof(TFlowService).FullName}'."));
  }

  public class ChildRule(Func<IMutationContext, IReadOnlyList<TEntity>, CancellationToken, Task> executor)
  {
    private readonly List<Func<Move<TEntity>, CancellationToken, Task>> _moveEffects = [];
    private readonly List<Func<Follow, CancellationToken, Task>> _followEffects = [];

    internal IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> Effects => _moveEffects;
    internal IReadOnlyList<Func<Follow, CancellationToken, Task>> FollowEffects => _followEffects;

    internal virtual PropertyInfo? GetParentKeyProperty(Type childEntityType) => null;

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
      await RunFollowEffectsAsync(context, records, ct);
    }

    protected async Task RunFollowEffectsAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken ct)
    {
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

    internal override PropertyInfo? GetParentKeyProperty(Type childEntityType) =>
      childEntityType == typeof(TChild) && _parentKey.Body is MemberExpression { Expression: ParameterExpression, Member: PropertyInfo pi }
        ? pi
        : null;

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
      await _parentFlow.LockAsync(context, parents, ct);

      if (_clauses.Count > 0)
      {
        var parentIds = parents.Select(p => p.Id).Distinct().ToList();
        var keyCompiled = _parentKey.Compile();
        var filterExpr = ParentScopeValidator.BuildKeyInFilter(_parentKey, parentIds);

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

      await RunFollowEffectsAsync(context, parents, ct);
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

  public PropertyInfo? FindParentKeyProperty(Type childEntityType)
  {
    EnsureDeclared();
    foreach (var rule in _childRules)
    {
      if (rule.GetParentKeyProperty(childEntityType) is { } prop)
      {
        return prop;
      }
    }

    return null;
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
      return BuildRecordTransitions(_publicTransitions.Where(IsTransitionPermitted));
    }
  }

  public IReadOnlyList<RecordTransition> AllTransitions
  {
    get
    {
      EnsureDeclared();
      return BuildRecordTransitions(_publicTransitions);
    }
  }

  private List<RecordTransition> BuildRecordTransitions(IEnumerable<Transition<TEntity>> transitions) =>
    transitions
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
        Label = t.LabelText,
        Title = t.TitleText,
        Confirm = t.Question,
        Ask = t.Asked,
        Offers = t.Availability
      })
      .ToList();

  private bool IsTransitionPermitted(Transition<TEntity> transition)
  {
    if (transition.RequiredRight is not { } reqRight)
    {
      return true;
    }

    if (User is not { IsAuthenticated: true })
    {
      return false;
    }

    return User.Can(reqRight) ||
           reqRight.Feature != null && User.Can(reqRight.Feature.All) ||
           User.Can("*");
  }

  public string NameOf(long status) =>
    Statuses.FirstOrDefault(option => option.Id == status)?.Name ?? status.ToString();

  public string? TransitionName(string? key)
  {
    if (string.IsNullOrEmpty(key))
    {
      return key;
    }

    EnsureDeclared();
    if (_transitionsByName.TryGetValue(key, out var t))
    {
      if (!string.IsNullOrEmpty(t.LabelText))
      {
        return L[t.LabelText];
      }

      if (!string.IsNullOrEmpty(Feature.Module) &&
          L.TryGetString($"{Feature.Module}: {t.Name}", out var moduleByName))
      {
        return moduleByName;
      }

      if (L.TryGetString(t.Name, out var byName))
      {
        return byName;
      }

      var spaced = Grid.Columns.PathToLabel(t.Name);
      var sentence = spaced.Contains(' ')
        ? string.Concat(spaced.AsSpan(0, 1), spaced[1..].ToLowerInvariant())
        : spaced;
      return L[sentence];
    }

    return key;
  }

  protected virtual void SetFlow(Flow flow) { }

  protected TransitionSource<TEntity> From(params long[] statuses)
  {
    EnsureDeclared();
    return new TransitionSource<TEntity>(statuses, t =>
    {
      _transitionsByName[t.Name] = t;
      return t;
    });
  }

  protected virtual Task OnMovedAsync(Move move, CancellationToken cancellationToken) => Task.CompletedTask;

  protected virtual ValueTask<string> RefusalAsync(Transition transition, long status, CancellationToken cancellationToken) =>
    new(L["StatusRefusesMove", L[NameOf(status)], L[NameOf(transition.To)]]);

  protected virtual Task LockAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken cancellationToken) =>
    Db.Database.CurrentTransaction != null
      ? context.LockAsync<TEntity>(records.Where(r => r.Id > 0).Select(r => new[] { r.Id }), cancellationToken)
      : Task.CompletedTask;

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

    if (!IsTransitionPermitted(transition))
    {
      context.AddError("status.permission", L["MutationRefused"]);
      return;
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
          : L["StatusRefusesMove", L[NameOf(currentStatus)], L[NameOf(transition.To)]];
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
    var moves = new List<(Move Move, long CurrentStatus)>(records.Count);

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
        var guardErrorsBefore = context.Errors.Count;
        if (!await passes(move, cancellationToken))
        {
          if (context.Errors.Count == guardErrorsBefore)
          {
            context.AddError("status.guard", refusal, record.Id, path);
          }

          return;
        }

        if (context.Errors.Count > guardErrorsBefore)
        {
          return;
        }
      }

      moves.Add((move, currentStatus));
    }

    foreach (var (move, currentStatus) in moves)
    {
      var record = move.Record;
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
      if (context.Errors.Count > errorsBefore)
      {
        _statusSetter?.Invoke(record, currentStatus);
        return;
      }
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

        if (PlatformApp != null && follower is IPlatformService ps)
        {
          ps.AttachPlatformApp(PlatformApp);
        }

        var parentEntityType = followerBase?.GetGenericArguments().FirstOrDefault();
        if (parentEntityType != null)
        {
          var fkProp = follower.FindParentKeyProperty(typeof(TEntity))
            ?? typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .FirstOrDefault(p => (p.PropertyType == typeof(long) || p.PropertyType == typeof(long?)) &&
                p.Name.Length > 2 &&
                p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(p.Name, parentEntityType.Name + "Id", StringComparison.OrdinalIgnoreCase) ||
                 parentEntityType.Name.EndsWith(p.Name[..^2], StringComparison.OrdinalIgnoreCase)));
          if (fkProp != null)
          {
            var parentIds = records
              .Select(r => fkProp.GetValue(r) is long id ? id : 0L)
              .Where(id => id > 0)
              .ToHashSet();
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
    foreach (var followerType in flow.FollowerTypes)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), followerType, "Flow.AddFollower");
    }

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
