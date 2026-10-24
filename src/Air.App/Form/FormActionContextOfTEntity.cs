namespace Platform.App.Form;

public sealed class FormActionContext<TEntity>(
  TEntity record,
  Func<CancellationToken, Task>? flusher = null,
  PlatformDbContext? db = null) : MutationContext(db)
  where TEntity : class, IEntity
{
  public TEntity Record { get; } = record;
  public TEntity Entity => Record;
  public string? OpenScreen { get; private set; }
  public long? OpenId { get; private set; }

  public void Refuse(string message, string? path = null) =>
    AddError("refused", message, Record.Id, path);

  public void Open<TFormService>(long? id = null) where TFormService : class
  {
    OpenScreen = ServicePathResolver.Resolve(typeof(TFormService));
    OpenId = id;
  }

  public override Task FlushAsync(CancellationToken cancellationToken) =>
    flusher != null ? flusher(cancellationToken) : base.FlushAsync(cancellationToken);
}
