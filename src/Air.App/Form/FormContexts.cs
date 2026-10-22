namespace Air.App.Form;

public sealed class FormRefusedException(
  string outcome,
  string title,
  IReadOnlyList<MessageDto> messages) : Exception(title)
{
  public string Outcome { get; } = outcome;
  public string Title { get; } = title;
  public IReadOnlyList<MessageDto> Messages { get; } = messages;
}

public class FormLoadContext<TEntity, TInput>
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
}

public class FormSaveContext<TEntity, TInput> : IMutationContext
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
  internal PlatformDbContext? Db { get; init; }
  internal IReadOnlyDictionary<string, object?>? OriginalValues { get; init; }
  private readonly List<MutationError> _mutationErrors = [];
  public List<MessageDto> Errors { get; } = [];

  IReadOnlyList<MutationError> IMutationContext.Errors => _mutationErrors;

  public void AddError(MutationError error)
  {
    _mutationErrors.Add(error);
    Errors.Add(error.ToMessage());
  }

  public void AddError(string detail, string? path = null)
  {
    _mutationErrors.Add(new MutationError("form.error", detail, path));
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });
  }

  public void AddError(string code, string detail, string? path = null)
  {
    _mutationErrors.Add(new MutationError(code, detail, path));
    Errors.Add(new MessageDto { Code = code, Detail = detail, Path = path, Type = MessageType.Error });
  }

  public void AddError(string code, string message, long? recordId, string? path = null)
  {
    _mutationErrors.Add(new MutationError(code, message, path, recordId));
    Errors.Add(new MessageDto { Code = code, Detail = message, Path = path, RecordId = recordId, Type = MessageType.Error });
  }

  public void Refuse(string detail, string? path = null) =>
    AddError(detail, path);

  public async Task FlushAsync(CancellationToken cancellationToken)
  {
    if (Db != null)
    {
      await Db.SaveChangesAsync(cancellationToken);
    }
  }

  public Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class =>
    MutationContext.AcquireLocksAsync(Db, typeof(TLocked), [key], cancellationToken);

  public Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class =>
    MutationContext.AcquireLocksAsync(Db, typeof(TLocked), keys, cancellationToken);

  public TProp Original<TProp>(Expression<Func<TInput, TProp>> property)
  {
    if (IsNew || OriginalValues is null)
    {
      return default!;
    }

    var body = property.Body is UnaryExpression ue ? ue.Operand : property.Body;
    if (body is MemberExpression me && me.Member is PropertyInfo pi &&
        OriginalValues.TryGetValue(pi.Name, out var origVal) &&
        origVal is TProp typed)
    {
      return typed;
    }

    return default!;
  }

  public bool Changed(params Expression<Func<TInput, object?>>[] properties)
  {
    if (IsNew || OriginalValues is null)
    {
      return true;
    }

    if (properties.Length == 0)
    {
      return true;
    }

    foreach (var propExpr in properties)
    {
      var body = propExpr.Body is UnaryExpression ue ? ue.Operand : propExpr.Body;
      if (body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        var currentVal = PropertyAccessorCache.GetValue(Input, pi.Name);
        OriginalValues.TryGetValue(pi.Name, out var origVal);
        if (!ValuesEqual(currentVal, origVal))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static bool ValuesEqual(object? a, object? b)
  {
    if (ReferenceEquals(a, b))
    {
      return true;
    }

    if (a is string sa && b is null)
    {
      return string.IsNullOrEmpty(sa);
    }

    if (a is null && b is string sb)
    {
      return string.IsNullOrEmpty(sb);
    }

    if (a is IEnumerable ea and not string and not byte[] && b is IEnumerable eb and not string and not byte[])
    {
      return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
    }

    return Equals(a, b);
  }
}

public class FormDeleteContext<TEntity>
{
  public required TEntity Entity { get; init; }
  public List<MessageDto> Errors { get; } = [];

  public void AddError(string detail, string? path = null) =>
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });

  public void AddError(string code, string detail, string? path = null) =>
    Errors.Add(new MessageDto { Code = code, Detail = detail, Path = path, Type = MessageType.Error });

  public void Refuse(string detail, string? path = null) =>
    AddError(detail, path);
}

public sealed class FormActionContext<TEntity>(
  TEntity record,
  Func<CancellationToken, Task>? flusher = null,
  PlatformDbContext? db = null) : MutationContext(db)
  where TEntity : class, IEntity
{
  public TEntity Record { get; } = record;
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
