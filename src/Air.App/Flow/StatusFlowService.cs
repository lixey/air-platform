using System.Linq.Expressions;
using System.Reflection;
using Air.App.Localization;
using Air.App.Metadata;
using Air.App.Mutations;
using Air.App.Screens;
using Air.Data;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Flow;

public abstract class StatusFlowService<TEntity>(PlatformDbContext db) : IRecordStatuses<TEntity>
  where TEntity : class, IEntity, new()
{
  protected PlatformDbContext Db => db;
  private readonly List<OptionDto> _statuses = [];
  private readonly Dictionary<string, Transition<TEntity>> _transitionsByName = new(StringComparer.OrdinalIgnoreCase);
  private Expression<Func<TEntity, long>>? _statusMember;
  private Action<TEntity, long>? _statusSetter;
  private OptionDto? _initial;
  private bool _isDeclared;

  public virtual Feature Feature => new("", GetType().Name.Replace("FlowService", "").ToLowerInvariant(), GetType());

  protected Localizer L => Localizer.Instance.For(Feature.Module);

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
      return _initial;
    }
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

    var note = transition.NoteOf(input);
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

      // Record move event (history)
      await OnMovedAsync(move, cancellationToken);
    }

    await context.FlushAsync(cancellationToken);
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
    if (flow.InitialStatusId.HasValue)
    {
      _initial = _statuses.FirstOrDefault(s => s.Id == flow.InitialStatusId.Value);
    }

    if (flow.StateMember != null)
    {
      _statusMember = flow.StateMember;
      if (_statusMember.Body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        _statusSetter = (entity, val) => pi.SetValue(entity, val);
      }
    }

    // Reflect transitions from properties
    var props = GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
      .Where(p => p.PropertyType == typeof(Transition<TEntity>));

    foreach (var p in props)
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
  }
}
