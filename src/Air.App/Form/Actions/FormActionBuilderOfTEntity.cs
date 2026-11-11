using Platform.App.Form.Internals;

namespace Platform.App.Form.Actions;

public class FormActionBuilder<TEntity>
  where TEntity : class, IEntity
{
  internal FormActionEntry<TEntity> Entry { get; }

  internal FormActionBuilder(FormActionEntry<TEntity> entry)
  {
    Entry = entry;
  }

  public FormActionBuilder<TEntity> Label(string label)
  {
    Entry.LabelText = label;
    return this;
  }

  public FormActionBuilder<TEntity> Title(string title)
  {
    Entry.TitleText = title;
    return this;
  }

  public FormActionBuilder<TEntity> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public FormActionBuilder<TEntity> Icon(string icon)
  {
    Entry.IconHandle = icon;
    return this;
  }

  public FormActionBuilder<TEntity> Color(string color)
  {
    Entry.ColorHandle = color;
    return this;
  }

  public FormActionBuilder<TEntity> Variant(string variant)
  {
    Entry.VariantHandle = variant;
    return this;
  }

  public FormActionBuilder<TEntity> Confirm(string? message)
  {
    Entry.ConfirmMessage = string.IsNullOrEmpty(message) ? null : message;
    return this;
  }

  public FormActionBuilder<TEntity> InMenu(bool inMenu = true)
  {
    Entry.IsInMenu = inMenu;
    return this;
  }

  public FormActionBuilder<TEntity> AllowReadOnly(bool allow = true)
  {
    Entry.IsAllowedInReadOnly = allow;
    return this;
  }

  public FormActionBuilder<TEntity> When(Expression<Func<TEntity, bool>> condition, string reason)
  {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    Entry.Offers.Add(new RecordAvailability { Condition = condition, Reason = reason });
    return this;
  }

  public FormActionBuilder<TEntity> Check(Func<TEntity, bool> guard, string refusal, string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((_, entity, _, _) => Task.FromResult(guard(entity)), refusal, path));
    return this;
  }

  public FormActionBuilder<TEntity> Check(
    Func<FormActionContext<TEntity>, TEntity, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((ctx, entity, _, ct) => guard(ctx, entity, ct), refusal, path));
    return this;
  }

  public FormActionBuilder<TEntity, TInput> Ask<TInput>(Action<Elements<TInput>> elements)
    where TInput : class, new()
  {
    ArgumentNullException.ThrowIfNull(elements);
    Entry.Ask = TransitionAsk.Of(elements);
    return new FormActionBuilder<TEntity, TInput>(Entry);
  }

  public FormActionBuilder<TEntity> Do(Func<FormActionContext<TEntity>, TEntity, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, entity, _, ct) => handler(ctx, entity, ct);
    return this;
  }

  public FormActionBuilder<TEntity> Do(Func<FormActionContext<TEntity>, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, _, _, ct) => handler(ctx, ct);
    return this;
  }

  public FormActionBuilder<TEntity> Do(Action<FormActionContext<TEntity>, TEntity> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, entity, _, _) =>
    {
      handler(ctx, entity);
      return Task.CompletedTask;
    };
    return this;
  }
}
