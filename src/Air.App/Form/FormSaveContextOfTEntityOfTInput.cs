namespace Platform.App.Form;

public class FormSaveContext<TEntity, TInput> : IMutationContext
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
  internal PlatformDbContext? Db { get; init; }
  internal IReadOnlyDictionary<string, object?>? OriginalValues { get; init; }
  private readonly List<MutationError> _mutationErrors = [];
  private readonly List<MutationError> _mutationWarnings = [];
  public List<MessageDto> Errors { get; } = [];
  public List<MessageDto> Warnings { get; } = [];

  IReadOnlyList<MutationError> IMutationContext.Errors => _mutationErrors;
  IReadOnlyList<MutationError> IMutationContext.Warnings => _mutationWarnings;

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

  public void Warn(MutationError warning)
  {
    _mutationWarnings.Add(warning);
    Warnings.Add(warning.ToMessage(MessageType.Warning));
  }

  public void Warn(string message, string? code = null, long? recordId = null, string? path = null)
  {
    var err = new MutationError(code ?? "form.warning", message, path, recordId);
    _mutationWarnings.Add(err);
    Warnings.Add(new MessageDto { Code = err.Code, Detail = message, Path = path, RecordId = recordId, Type = MessageType.Warning });
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
