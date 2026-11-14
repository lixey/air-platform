namespace Platform.App.Form.Actions;

public sealed class FormActionBuilder<TEntity, TInput> : FormActionBuilder<TEntity>
  where TEntity : class, IEntity
  where TInput : class, new()
{
  internal FormActionBuilder(FormActionItem<TEntity> entry) : base(entry)
  {
  }

  public new FormActionBuilder<TEntity, TInput> Label(string label)
  {
    base.Label(label);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Icon(string icon)
  {
    base.Icon(icon);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Color(string color)
  {
    base.Color(color);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Variant(string variant)
  {
    base.Variant(variant);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Confirm(string? message)
  {
    base.Confirm(message);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> InMenu(bool inMenu = true)
  {
    base.InMenu(inMenu);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> AllowReadOnly(bool allow = true)
  {
    base.AllowReadOnly(allow);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> When(Expression<Func<TEntity, bool>> condition, string reason)
  {
    base.When(condition, reason);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Check(Func<TEntity, bool> guard, string refusal, string? path = null)
  {
    base.Check(guard, refusal, path);
    return this;
  }

  public new FormActionBuilder<TEntity, TInput> Check(
    Func<FormActionContext<TEntity>, TEntity, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    base.Check(guard, refusal, path);
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Check(Func<TEntity, TInput, bool> guard, string refusal, string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((_, entity, input, _) => Task.FromResult(guard(entity, input as TInput ?? new TInput())), refusal, path));
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Check(
    Func<FormActionContext<TEntity>, TEntity, TInput, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((ctx, entity, input, ct) => guard(ctx, entity, input as TInput ?? new TInput(), ct), refusal, path));
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Default(Func<TEntity?, CancellationToken, Task<TInput>> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = async (entity, ct) => await provider(entity, ct);
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Default(Func<CancellationToken, Task<TInput>> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = async (_, ct) => await provider(ct);
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Default(Func<TEntity?, TInput> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = (entity, _) => Task.FromResult<object?>(provider(entity));
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Default(Func<TInput> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    Entry.DefaultProvider = (_, _) => Task.FromResult<object?>(provider());
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Default(Func<TInput, CancellationToken, Task> configure)
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

  public FormActionBuilder<TEntity, TInput> Default(Action<TInput> configure)
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

  public FormActionBuilder<TEntity, TInput> Do(
    Func<FormActionContext<TEntity>, TEntity, TInput, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, entity, input, ct) => handler(ctx, entity, input as TInput ?? new TInput(), ct);
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Do(
    Func<FormActionContext<TEntity>, TInput, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, _, input, ct) => handler(ctx, input as TInput ?? new TInput(), ct);
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Do(Action<FormActionContext<TEntity>, TEntity, TInput> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, entity, input, _) =>
    {
      handler(ctx, entity, input as TInput ?? new TInput());
      return Task.CompletedTask;
    };
    return this;
  }
}
