namespace Platform.App.Form;

public sealed class FormHooks<TEntity>
  where TEntity : class
{
  public List<Func<FormLoadContext<TEntity>, CancellationToken, Task>> AfterLoadActions { get; } = [];
  public List<Action<FormSaveContext<TEntity>>> ComputeActions { get; } = [];
  public List<Func<FormSaveContext<TEntity>, CancellationToken, Task>> BeforeSaveActions { get; } = [];
  public List<Func<FormSaveContext<TEntity>, CancellationToken, Task>> AfterSaveActions { get; } = [];

  public void AfterLoad(Func<FormLoadContext<TEntity>, CancellationToken, Task> handler) => AfterLoadActions.Add(handler);

  public void AfterLoad(Action<FormLoadContext<TEntity>> handler) =>
    AfterLoadActions.Add((c, _) =>
    {
      handler(c);
      return Task.CompletedTask;
    });

  public void Compute(Action<FormSaveContext<TEntity>> handler) => ComputeActions.Add(handler);

  public void BeforeSave(Func<FormSaveContext<TEntity>, CancellationToken, Task> handler) => BeforeSaveActions.Add(handler);

  public void BeforeSave(Action<FormSaveContext<TEntity>> handler) =>
    BeforeSaveActions.Add((c, _) =>
    {
      handler(c);
      return Task.CompletedTask;
    });

  public void AfterSave(Func<FormSaveContext<TEntity>, CancellationToken, Task> handler) => AfterSaveActions.Add(handler);

  public void AfterSave(Action<FormSaveContext<TEntity>> handler) =>
    AfterSaveActions.Add((c, _) =>
    {
      handler(c);
      return Task.CompletedTask;
    });
}
