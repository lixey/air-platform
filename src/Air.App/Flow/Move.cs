namespace Platform.App.Flow;

public class Move<TEntity>
{
  public required TEntity Record { get; init; }
  public required long From { get; init; }
  public required long To { get; init; }
  public required string Transition { get; init; }
  public string? Note { get; init; }
  public long? UserId { get; init; }
  public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
  public required IMutationContext Context { get; init; }
  public object? Input { get; init; }

  public TInput InputOf<TInput>() where TInput : class =>
    Input as TInput ?? throw new InvalidOperationException($"Expected transition input of type '{typeof(TInput).Name}'.");
}
