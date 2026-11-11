using Platform.App.Flow.Internals;

namespace Platform.App.Flow;

public abstract partial class StatusFlowService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : IRecordStatuses<TEntity>, IPlatformService
  where TEntity : class, IEntity, new()
{
  protected PlatformDbContext Db => db;
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;

  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  private readonly List<OptionDto> _statuses = [];
  private readonly HashSet<long> _finalStatusIds = [];
  private readonly List<Type> _followers = [];
  private readonly List<ChildRule> _childRules = [];
  private readonly Dictionary<string, Transition<TEntity>> _transitionsByName = new(StringComparer.OrdinalIgnoreCase);
  private Expression<Func<TEntity, long>>? _statusMember;
  private Func<TEntity, long>? _statusReader;
  private Action<TEntity, long>? _statusSetter;
  private long? _initialStatusId;
  private bool _isDeclared;
  private bool _isDeclaring;

  public abstract Feature Feature { get; }

  IEnumerable<string> IPlatformService.GetDeclaredActions()
  {
    EnsureDeclared();
    return _publicTransitions
      .Select(t => t.Name)
      .Where(n => !string.IsNullOrWhiteSpace(n));
  }

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    EnsureDeclared();
    if (_statusMember == null)
    {
      throw new InvalidOperationException(
        $"Status flow '{GetType().FullName}' must declare a status property via flow.State(...).");
    }

    var seenStatusIds = new HashSet<long>();
    foreach (var status in _statuses)
    {
      if (!seenStatusIds.Add(status.Id))
      {
        throw new InvalidOperationException(
          $"Status flow '{GetType().FullName}' declares duplicate status ID '{status.Id}'.");
      }
    }

    if (seenStatusIds.Count > 0)
    {
      if (_initialStatusId.HasValue && !seenStatusIds.Contains(_initialStatusId.Value))
      {
        throw new InvalidOperationException(
          $"Status flow '{GetType().FullName}' sets initial status ID '{_initialStatusId.Value}' which is not declared in flow.Status(...).");
      }

      foreach (var transition in _transitionsByName.Values)
      {
        transition.Asked?.ValidateConfiguration(GetType());
        if (transition.RequiredRight?.Feature?.ServiceType is { } rightServiceType)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), rightServiceType, $"Transition.Allow on '{transition.Name}'");
        }

        if (!seenStatusIds.Contains(transition.To))
        {
          throw new InvalidOperationException(
            $"Transition '{transition.Name}' in '{GetType().FullName}' targets undeclared status ID '{transition.To}'.");
        }

        foreach (var fromId in transition.From)
        {
          if (!seenStatusIds.Contains(fromId))
          {
            throw new InvalidOperationException(
              $"Transition '{transition.Name}' in '{GetType().FullName}' references undeclared source status ID '{fromId}'.");
          }
        }
      }
    }
    else
    {
      foreach (var transition in _transitionsByName.Values)
      {
        transition.Asked?.ValidateConfiguration(GetType());
        if (transition.RequiredRight?.Feature?.ServiceType is { } rightServiceType)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), rightServiceType, $"Transition.Allow on '{transition.Name}'");
        }
      }
    }
  }

  protected Localizer L => Localizer.Instance.For(Feature.Module);

  public IReadOnlyList<Type> Followers
  {
    get
    {
      EnsureDeclared();
      return FlowFollowerRegistry.GetFollowers(GetType(), _followers, Db, PlatformApp);
    }
  }

  private (IMutationContext Context, HashSet<long> Ids)? _preLockedBatch;

  bool IRecordStatuses.FollowsChildFlow(Type childFlowType)
  {
    EnsureDeclared();
    return _childRules.Any(r => r.ReferencesChildFlow(childFlowType));
  }

  async Task IRecordStatuses<TEntity>.LockRecordsAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    CancellationToken cancellationToken)
  {
    var ordered = records.Count > 1 ? records.OrderBy(r => r.Id).ToList() : records;
    await LockAsync(context, ordered, cancellationToken);
    _preLockedBatch = (context, ordered.Where(r => r.Id > 0).Select(r => r.Id).ToHashSet());
  }

  protected TFlowService FlowOf<TFlowService>() where TFlowService : class
  {
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), typeof(TFlowService), nameof(FlowOf));
    return (TFlowService)(PlatformApp?.Services.GetService(typeof(TFlowService))
      ?? ModuleCatalog.Instance.CreateServiceInstance(typeof(TFlowService), Db, PlatformApp)
      ?? throw new InvalidOperationException($"Cannot resolve or instantiate flow service '{typeof(TFlowService).FullName}'."));
  }

  protected ChildFlowRuleBuilder<TChild> Children<TChild>(
    StatusFlowService<TChild> childFlow,
    Expression<Func<TChild, long>> parentKey)
    where TChild : class, IEntity, new()
  {
    var rule = new ChildFlowRuleBuilder<TChild>(this, childFlow, parentKey);
    if (_isDeclaring && !_childRules.Any(r => r.ReferencesChildFlow(childFlow.GetType()) && r.GetParentKeyProperty(typeof(TChild)) == rule.GetParentKeyProperty(typeof(TChild))))
    {
      _childRules.Add(rule);
    }

    return rule;
  }

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

    var orderedIds = movedRecordIds.OrderBy(id => id).ToList();
    if (Db.Database.CurrentTransaction != null)
    {
      var candidates = await Db.Set<TEntity>()
        .AsNoTracking()
        .Where(e => orderedIds.Contains(e.Id))
        .OrderBy(e => e.Id)
        .ToListAsync(cancellationToken);
      if (candidates.Count == 0)
      {
        return;
      }

      await LockAsync(context, candidates, cancellationToken);
      foreach (var tracked in Db.Set<TEntity>().Local.Where(e => orderedIds.Contains(e.Id)).ToList())
      {
        if (Db.Entry(tracked).State == EntityState.Unchanged && Db.Database.IsRelational())
        {
          await Db.Entry(tracked).ReloadAsync(cancellationToken);
        }
      }
    }

    var parents = await Db.Set<TEntity>()
      .AsTracking()
      .Where(e => orderedIds.Contains(e.Id))
      .OrderBy(e => e.Id)
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
    Statuses.FirstOrDefault(option => option.Id == status) is { } opt
      ? L[opt.Name]
      : status.ToString(CultureInfo.InvariantCulture);

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

  private Transition RegisterTransition(Transition t)
  {
    _transitionsByName[t.Name] = t;
    return t;
  }

  protected TransitionSource<TEntity> From(params long[] statuses)
  {
    EnsureDeclared();
    return new TransitionSource<TEntity>(statuses, RegisterTransition);
  }

  protected TransitionSource<TEntity> FromOpen()
  {
    EnsureDeclared();
    var openIds = _statuses
      .Where(s => !_finalStatusIds.Contains(s.Id))
      .Select(s => s.Id)
      .ToArray();
    return new TransitionSource<TEntity>(openIds, RegisterTransition, excludeTarget: true);
  }

  protected TransitionSource<TEntity> FromOpenExcept(params long[] excludedStatuses)
  {
    EnsureDeclared();
    var excluded = excludedStatuses.ToHashSet();
    var openIds = _statuses
      .Where(s => !_finalStatusIds.Contains(s.Id) && !excluded.Contains(s.Id))
      .Select(s => s.Id)
      .ToArray();
    return new TransitionSource<TEntity>(openIds, RegisterTransition, excludeTarget: true);
  }

  protected virtual Task OnMovedAsync(Move move, CancellationToken cancellationToken) => Task.CompletedTask;

  protected virtual ValueTask<string> RefusalAsync(Transition transition, long status, CancellationToken cancellationToken) =>
    new(L["StatusRefusesMove", NameOf(status), NameOf(transition.To)]);

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
    var preLocked = _preLockedBatch;
    _preLockedBatch = null;
    if (records.Count == 0)
    {
      return;
    }

    records = records.Count > 1 ? records.OrderBy(r => r.Id).ToList() : records;
    var alreadyLocked = preLocked is { } pre &&
      ReferenceEquals(pre.Context, context) &&
      records.Where(r => r.Id > 0).All(r => pre.Ids.Contains(r.Id));
    if (!alreadyLocked)
    {
      await LockAsync(context, records, cancellationToken);
    }

    var statusReader = _statusReader ??= _statusMember!.Compile();
    foreach (var record in records)
    {
      var currentStatus = statusReader(record);
      if (!transition.From.Contains(currentStatus))
      {
        var refusal = transition is Transition nestedTransition
          ? await RefusalAsync(nestedTransition, currentStatus, cancellationToken)
          : L["StatusRefusesMove", NameOf(currentStatus), NameOf(transition.To)];
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

    var followers = FlowFollowerRegistry.GetFollowers(GetType(), _followers, Db, PlatformApp);
    if (followers.Count > 0)
    {
      foreach (var followerType in followers)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(followerType, GetType(), "Flow.Children");
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
              .Select(r => fkProp.GetValue(r) is { } raw ? Convert.ToInt64(raw, CultureInfo.InvariantCulture) : 0L)
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

  private static Action<TEntity, long>? BuildStatusSetter(Expression body)
  {
    var unwrapped = body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } ue
      ? ue.Operand
      : body;
    if (unwrapped is not MemberExpression { Member: PropertyInfo { CanWrite: true } pi })
    {
      return null;
    }

    var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
    if (targetType == typeof(long))
    {
      return (entity, val) => pi.SetValue(entity, val);
    }

    if (targetType.IsEnum)
    {
      return (entity, val) => pi.SetValue(entity, Enum.ToObject(targetType, val));
    }

    return (entity, val) => pi.SetValue(entity, Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture));
  }

  private void EnsureDeclared()
  {
    if (_isDeclared)
    {
      return;
    }

    _isDeclared = true;
    _isDeclaring = true;
    try
    {
      var flow = new Flow();
      SetFlow(flow);

      _statuses.AddRange(flow.DeclaredStatuses);
      foreach (var finalId in flow.FinalStatusIds)
      {
        _finalStatusIds.Add(finalId);
      }

      foreach (var followerType in flow.FollowerTypes)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(followerType, GetType(), "Flow.FollowedBy");
      }

      _followers.AddRange(flow.FollowerTypes);
      _initialStatusId = flow.InitialStatusId;
      if (flow.InitialStatusId.HasValue)
      {
        Initial = _statuses.FirstOrDefault(s => s.Id == flow.InitialStatusId.Value);
      }

      if (flow.StateMember != null)
      {
        _statusMember = flow.StateMember;
        _statusReader = _statusMember.Compile();
        _statusSetter = BuildStatusSetter(_statusMember.Body);
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
        if (p.GetValue(this) is ChildRule rule && !_childRules.Contains(rule))
        {
          _childRules.Add(rule);
        }
      }
    }
    finally
    {
      _isDeclaring = false;
    }
  }
}
