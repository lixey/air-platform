using Platform.App.Flow.Internals;
using Platform.App.Internals;

namespace Platform.App.Flow;

public abstract partial class StatusFlowService<TEntity>
  where TEntity : class, IEntity, new()
{
  public sealed class Move : Move<TEntity>;

  public sealed class Follow
  {
    public required TEntity Record { get; init; }
    public required IMutationContext Context { get; init; }
  }

  public sealed class Transition(string name, IReadOnlyList<long> from, long to) : Transition<TEntity>(name, from, to)
  {
    public new Transition Check(Func<TEntity, bool> guard, string refusal, string? path = null)
    {
      base.Check(guard, refusal, path);
      return this;
    }

    public new Transition Check(Func<Move<TEntity>, CancellationToken, Task<bool>> guard, string refusal, string? path = null)
    {
      base.Check(guard, refusal, path);
      return this;
    }

    public Transition Check(Func<Move, CancellationToken, Task<bool>> guard, string refusal, string? path = null)
    {
      ArgumentNullException.ThrowIfNull(guard);
      base.Check((m, ct) => guard((Move)m, ct), refusal, path);
      return this;
    }

    public new Transition When(Expression<Func<TEntity, bool>> condition, string reason)
    {
      base.When(condition, reason);
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

    public new Transition Ask<TInput>(Action<Elements<TInput>> elements, Func<TInput, string?>? note = null)
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

    public new Transition Allow(Right right)
    {
      base.Allow(right);
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

  public class ChildRule(Func<IMutationContext, IReadOnlyList<TEntity>, CancellationToken, Task> executor)
  {
    private readonly List<Func<Move<TEntity>, CancellationToken, Task>> _moveEffects = [];
    private readonly List<Func<Follow, CancellationToken, Task>> _followEffects = [];

    internal IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> Effects => _moveEffects;
    internal IReadOnlyList<Func<Follow, CancellationToken, Task>> FollowEffects => _followEffects;

    internal virtual PropertyInfo? GetParentKeyProperty(Type childEntityType) => null;

    internal virtual bool ReferencesChildFlow(Type childFlowType) => false;

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
      : base((_, _, _) => Task.CompletedTask)
    {
      _parentFlow = parentFlow;
      _childFlow = childFlow;
      _parentKey = parentKey;
      FlowFollowerRegistry.Register(childFlow.GetType(), parentFlow.GetType());
    }

    private readonly StatusFlowService<TEntity> _parentFlow;
    private readonly StatusFlowService<TChild> _childFlow;
    private readonly Expression<Func<TChild, long>> _parentKey;

    internal override bool ReferencesChildFlow(Type childFlowType) =>
      childFlowType.IsAssignableFrom(_childFlow.GetType()) || _childFlow.GetType().IsAssignableFrom(childFlowType);

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
}
