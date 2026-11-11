using Platform.App.Grid.Internals;

namespace Platform.App.Grid.Actions;

public class GridActionBuilder<TEntity>
  where TEntity : class, IEntity
{
  internal GridActionEntry<TEntity> Entry { get; }

  internal GridActionBuilder(GridActionEntry<TEntity> entry)
  {
    Entry = entry;
  }

  public GridActionBuilder<TEntity> Label(string label)
  {
    Entry.LabelText = label;
    return this;
  }

  public GridActionBuilder<TEntity> Title(string title)
  {
    Entry.TitleText = title;
    return this;
  }

  public GridActionBuilder<TEntity> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public GridActionBuilder<TEntity> Icon(string icon)
  {
    Entry.IconHandle = icon;
    return this;
  }

  public GridActionBuilder<TEntity> Color(string color)
  {
    Entry.ColorHandle = color;
    return this;
  }

  public GridActionBuilder<TEntity> Variant(string variant)
  {
    Entry.VariantHandle = variant;
    return this;
  }

  public GridActionBuilder<TEntity> Confirm(string? message)
  {
    Entry.ConfirmMessage = string.IsNullOrEmpty(message) ? null : message;
    return this;
  }

  public GridActionBuilder<TEntity> InMenu(bool inMenu = true)
  {
    Entry.IsInMenu = inMenu;
    return this;
  }

  public GridActionBuilder<TEntity> AllowReadOnly(bool allow = true)
  {
    Entry.IsAllowedInReadOnly = allow;
    return this;
  }

  public GridActionBuilder<TEntity> Single(bool single = true)
  {
    Entry.IsSingle = single;
    return this;
  }

  public GridActionBuilder<TEntity> WithoutSelection()
  {
    Entry.NeedsSelection = false;
    Entry.IgnoresSelection = true;
    Entry.IsSingle = false;
    return this;
  }

  public GridActionBuilder<TEntity> OptionalSelection()
  {
    Entry.NeedsSelection = false;
    Entry.IgnoresSelection = false;
    return this;
  }

  public GridActionBuilder<TEntity> WithoutTransaction(bool withoutTransaction = true)
  {
    Entry.IsWithoutTransaction = withoutTransaction;
    return this;
  }

  public GridActionBuilder<TEntity> When(Expression<Func<TEntity, bool>> condition, string reason)
  {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    Entry.Offers.Add(new RecordAvailability { Condition = condition, Reason = reason });
    return this;
  }

  public GridActionBuilder<TEntity> Check(Func<TEntity, bool> guard, string refusal, string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((ctx, records, _, l, _) =>
    {
      foreach (var record in records)
      {
        if (!guard(record))
        {
          ctx.AddError("grid.guard", l[refusal], record.Id, path);
          return Task.FromResult(false);
        }
      }

      return Task.FromResult(true);
    }, refusal, path));
    return this;
  }

  public GridActionBuilder<TEntity> Check(
    Func<GridBatchContext<TEntity>, CancellationToken, Task<bool>> guard,
    string refusal,
    string? path = null)
  {
    ArgumentNullException.ThrowIfNull(guard);
    ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
    Entry.Guards.Add(((ctx, _, _, _, ct) => guard(ctx, ct), refusal, path));
    return this;
  }

  public GridActionBuilder<TEntity, TInput> Ask<TInput>(Action<Elements<TInput>> elements)
    where TInput : class, new()
  {
    ArgumentNullException.ThrowIfNull(elements);
    Entry.Ask = TransitionAsk.Of(elements);
    return new GridActionBuilder<TEntity, TInput>(Entry);
  }

  public GridActionBuilder<TEntity> Do(Func<GridBatchContext<TEntity>, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, _, ct) => handler(ctx, ct);
    return this;
  }

  public GridActionBuilder<TEntity> Do(Func<GridBatchContext<TEntity>, TEntity, CancellationToken, Task> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.IsSingle = true;
    Entry.Handler = (ctx, _, ct) => handler(ctx, ctx.Records[0], ct);
    return this;
  }

  public GridActionBuilder<TEntity> Do(Action<GridBatchContext<TEntity>, TEntity> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.IsSingle = true;
    Entry.Handler = (ctx, _, _) =>
    {
      handler(ctx, ctx.Records[0]);
      return Task.CompletedTask;
    };
    return this;
  }

  public GridActionBuilder<TEntity> Do(Action<GridBatchContext<TEntity>> handler)
  {
    ArgumentNullException.ThrowIfNull(handler);
    Entry.Handler = (ctx, _, _) =>
    {
      handler(ctx);
      return Task.CompletedTask;
    };
    return this;
  }
}
