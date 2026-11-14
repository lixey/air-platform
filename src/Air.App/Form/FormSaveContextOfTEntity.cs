using Platform.App.Internals;

namespace Platform.App.Form;

public class FormSaveContext<TEntity>(PlatformDbContext? db = null) : MutationContext(db)
{
  private Dictionary<string, object?>? _items;

  public required TEntity Entity { get; init; }
  public bool IsNew { get; init; }
  internal IReadOnlyDictionary<string, object?>? OriginalValues { get; init; }
  internal IReadOnlySet<string>? PresentPaths { get; init; }
  public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(StringComparer.Ordinal);

  public T? GetExecutionItem<T>(string key) =>
    _items is not null && _items.TryGetValue(key, out var value) && value is T typed
      ? typed
      : default;

  public void SetExecutionItem<T>(string key, T value) =>
    Items[key] = value;

  public T? GetExecutionItem<T>() =>
    GetExecutionItem<T>(typeof(T).FullName ?? typeof(T).Name);

  public void SetExecutionItem<T>(T value) =>
    SetExecutionItem(typeof(T).FullName ?? typeof(T).Name, value);

  public bool HasInput(string path) =>
    !string.IsNullOrEmpty(path) && PresentPaths != null && PresentPaths.Contains(path);

  public bool HasInput<TProp>(Expression<Func<TEntity, TProp>> property)
  {
    var body = property.Body is UnaryExpression ue ? ue.Operand : property.Body;
    return body is MemberExpression me && HasInput(me.Member.Name);
  }

  public void AddError(string detail) =>
    AddError(new MutationError("form.error", detail));

  public void AddError(string code, string detail, string? path) =>
    AddError(new MutationError(code, detail, path));

  public void AddError<TProp>(Expression<Func<TEntity, TProp>> property, string detail, string code = "form.error")
  {
    var body = property.Body is UnaryExpression ue ? ue.Operand : property.Body;
    var path = body is MemberExpression me ? Elements.ToCamelPath(me.Member.Name) : null;
    AddError(new MutationError(code, detail, path));
  }

  public void Refuse(string detail, string? path = null) =>
    AddError(new MutationError("form.error", detail, path));

  internal List<MessageDto> ToErrorMessages() =>
    [.. Errors.Select(static e => ToFormMessage(e, MessageType.Error, "form.error"))];

  internal List<MessageDto> ToWarningMessages() =>
    [.. Warnings.Select(static w => ToFormMessage(w, MessageType.Warning, null))];

  private static MessageDto ToFormMessage(MutationError item, string type, string? omitDefaultCode) => new()
  {
    Code = omitDefaultCode != null && string.Equals(item.Code, omitDefaultCode, StringComparison.Ordinal) ? null : item.Code,
    Detail = item.Title,
    Path = item.Path,
    RecordId = item.RecordId,
    RecordLabel = item.RecordLabel,
    Type = type
  };

  public TProp Original<TProp>(Expression<Func<TEntity, TProp>> property)
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

  public bool Changed(params Expression<Func<TEntity, object?>>[] properties)
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
        var currentVal = PropertyAccessorCache.GetValue(Entity, pi.Name);
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
