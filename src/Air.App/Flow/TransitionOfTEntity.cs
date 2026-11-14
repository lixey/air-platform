namespace Platform.App.Flow;

public class Transition<TEntity>
{
  private readonly List<(Func<Move<TEntity>, CancellationToken, Task<bool>> Passes, string Refusal, string? Path)> _guards = [];
  private readonly List<Func<Move<TEntity>, CancellationToken, Task>> _effects = [];
  private readonly List<RecordAvailability> _availability = [];
  private Func<object, string?>? _note;

  protected internal Transition(string name, IReadOnlyList<long> from, long to)
  {
    Name = name;
    From = from;
    To = to;
  }

  public string Name { get; }
  public IReadOnlyList<long> From { get; }
  public long To { get; }
  public string? LabelText { get; private set; }
  public string? TitleText { get; private set; }
  public string? Question { get; private set; }
  public RecordAsk? Asked { get; private set; }
  public Right? RequiredRight { get; private set; }
  public IReadOnlyList<RecordAvailability> Availability => _availability;
  public IReadOnlyList<(Func<Move<TEntity>, CancellationToken, Task<bool>> Passes, string Refusal, string? Path)> Guards => _guards;
  public IReadOnlyList<Func<Move<TEntity>, CancellationToken, Task>> Effects => _effects;

  public string? NoteOf(object? input) => input is null ? null : _note?.Invoke(input);

  public Transition<TEntity> Label(string label)
  {
    LabelText = label;
    return this;
  }

  public Transition<TEntity> Title(string title)
  {
    TitleText = title;
    return this;
  }

  public Transition<TEntity> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public Transition<TEntity> When(Expression<Func<TEntity, bool>> condition, string reason)
  {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentNullException.ThrowIfNull(reason);
    _availability.Add(new RecordAvailability { Condition = condition, Reason = reason });
    return this;
  }

  public Transition<TEntity> Check(Func<TEntity, bool> guard, string refusal, string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    return Check((move, _) => Task.FromResult(guard(move.Record)), refusal, path);
  }

  public Transition<TEntity> Check(Func<Move<TEntity>, CancellationToken, Task<bool>> guard, string refusal, string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentNullException.ThrowIfNull(refusal);
    _guards.Add((guard, refusal, path));
    return this;
  }

  public Transition<TEntity> Do(Func<Move<TEntity>, CancellationToken, Task> effect)
  {
    ArgumentNullException.ThrowIfNull(effect);
    _effects.Add(effect);
    return this;
  }

  public Transition<TEntity> Ask<TInput>(Action<Elements<TInput>> elements, Func<TInput, string?>? note = null)
    where TInput : class, new()
  {
    ArgumentNullException.ThrowIfNull(elements);
    Asked = TransitionAsk.Of(elements);
    _note = note is null ? null : input => input is TInput typed ? note(typed) : null;
    return this;
  }

  public Transition<TEntity> Confirm(string question)
  {
    Question = question;
    return this;
  }

  public Transition<TEntity> Allow(Right right)
  {
    ArgumentNullException.ThrowIfNull(right);
    RequiredRight = right;
    return this;
  }
}
