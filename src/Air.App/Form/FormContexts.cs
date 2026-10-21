using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Air.App.Internal;
using Air.Data.Contracts;

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

public class FormSaveContext<TEntity, TInput>
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
  internal IReadOnlyDictionary<string, object?>? OriginalValues { get; init; }
  public List<MessageDto> Errors { get; } = [];

  public void AddError(string detail, string? path = null) =>
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });

  public void AddError(string code, string detail, string? path = null) =>
    Errors.Add(new MessageDto { Code = code, Detail = detail, Path = path, Type = MessageType.Error });

  public void Refuse(string detail, string? path = null) =>
    AddError(detail, path);

  public TProp Original<TProp>(Expression<Func<TInput, TProp>> property)
  {
    if (IsNew || OriginalValues is null) return default!;
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
    if (IsNew || OriginalValues is null) return true;
    if (properties.Length == 0) return true;

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
    if (ReferenceEquals(a, b)) return true;
    if (a is string sa && b is null) return string.IsNullOrEmpty(sa);
    if (a is null && b is string sb) return string.IsNullOrEmpty(sb);
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
