using Platform.App.Grid.Internals;

namespace Platform.App.Grid.Actions;

public sealed class GridActionBuilder<TEntity, TInput> : GridActionBuilder<TEntity>
  where TEntity : class, IEntity
  where TInput : class, new()
{
  internal GridActionBuilder(GridActionEntry<TEntity> entry) : base(entry)
  {
  }

  public new GridActionBuilder<TEntity, TInput> Label(string label)
  {
    base.Label(label);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Icon(string icon)
  {
    base.Icon(icon);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Color(string color)
  {
    base.Color(color);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Variant(string variant)
  {
    base.Variant(variant);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Confirm(string? message)
  {
    base.Confirm(message);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> InMenu(bool inMenu = true)
  {
    base.InMenu(inMenu);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> AllowReadOnly(bool allow = true)
  {
    base.AllowReadOnly(allow);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Single(bool single = true)
  {
    base.Single(single);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> WithoutSelection()
  {
    base.WithoutSelection();
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> OptionalSelection()
  {
    base.OptionalSelection();
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> WithoutTransaction(bool withoutTransaction = true)
  {
    base.WithoutTransaction(withoutTransaction);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> When(Expression<Func<TEntity, bool>> condition, string reason)
  {
    base.When(condition, reason);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Check(Func<TEntity, bool> guard, string refusal, string? path = null)
  {
    base.Check(guard, refusal, path);
    return this;
  }

  public new GridActionBuilder<TEntity, TInput> Check(
    Func<GridBatchContext<TEntity>, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    base.Check(guard, refusal, path);
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Check(
    Func<GridBatchContext<TEntity>, TInput, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((ctx, _, input, _, ct) => guard(ctx, input as TInput ?? new TInput(), ct), refusal, path));
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Default(Func<IReadOnlyList<long>, CancellationToken, Task<TInput>> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = async (selectedIds, ct) => await provider(selectedIds, ct);
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Default(Func<CancellationToken, Task<TInput>> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = async (_, ct) => await provider(ct);
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Default(Func<TInput> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = (_, _) => Task.FromResult<object?>(provider());
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Default(Func<TInput, CancellationToken, Task> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Entry.DefaultProvider = async (_, ct) =>
    {
      var input = new TInput();
      await configure(input, ct);
      return input;
    };
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Default(Action<TInput> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Entry.DefaultProvider = (_, _) =>
    {
      var input = new TInput();
      configure(input);
      return Task.FromResult<object?>(input);
    };
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Do(
    Func<GridBatchContext<TEntity>, TInput, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, input, ct) => handler(ctx, input as TInput ?? new TInput(), ct);
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Do(
    Func<GridBatchContext<TEntity>, TEntity, TInput, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.IsSingle = true;
    Entry.Handler = (ctx, input, ct) => handler(ctx, ctx.Records[0], input as TInput ?? new TInput(), ct);
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Do(Action<GridBatchContext<TEntity>, TEntity, TInput> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.IsSingle = true;
    Entry.Handler = (ctx, input, _) =>
    {
      handler(ctx, ctx.Records[0], input as TInput ?? new TInput());
      return Task.CompletedTask;
    };
    return this;
  }
}
