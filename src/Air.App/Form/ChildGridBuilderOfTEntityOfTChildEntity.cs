using Platform.Data.Internals;

namespace Platform.App.Form;

public sealed class ChildGridBuilder<TEntity, TChildEntity>(
  string servicePath,
  Type? serviceType = null,
  string? tab = null) : ChildGridBuilder(servicePath, serviceType, tab)
  where TEntity : class, IEntity
  where TChildEntity : class, IEntity
{
  public new ChildGridBuilder<TEntity, TChildEntity> Key(string key)
  {
    base.Key(key);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> Label(string label)
  {
    base.Label(label);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> ReadOnly(bool readOnly = true)
  {
    base.ReadOnly(readOnly);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> AsFiles(string look = FilesLook.Tiles)
  {
    base.AsFiles(look);
    return this;
  }

  public new ChildGridBuilder<TEntity, TChildEntity> SumTo(string parentFieldPath, string childFieldPath)
  {
    base.SumTo(parentFieldPath, childFieldPath);
    return this;
  }

  public ChildGridBuilder<TEntity, TChildEntity> SumTo<TParentVal, TChildVal>(
    Expression<Func<TEntity, TParentVal>> parentField,
    Expression<Func<TChildEntity, TChildVal>> childField)
  {
    ArgumentNullException.ThrowIfNull(parentField);
    ArgumentNullException.ThrowIfNull(childField);

    var parentExpr = parentField.Body is UnaryExpression ue ? ue.Operand : parentField.Body;
    if (parentExpr is not MemberExpression { Expression: ParameterExpression, Member: PropertyInfo { CanWrite: true } parentProp })
    {
      throw new ArgumentException("SumTo parentField must reference a writable scalar property on the parent entity.", nameof(parentField));
    }

    EnsureNumericType(parentProp.PropertyType, nameof(parentField));
    EnsureNumericType(typeof(TChildVal), nameof(childField));

    var parentPath = Elements.ToCamelPath(parentProp.Name);
    var rawChildPath = MemberPath.Of(childField);
    var childPath = !string.IsNullOrEmpty(rawChildPath) ? Elements.ToCamelPath(rawChildPath) : parentPath;

    AddSumMapping(new ChildSumMapping(parentPath, childPath, parentProp, childField));
    return this;
  }

  private static void EnsureNumericType(Type type, string paramName)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying != typeof(decimal) &&
        underlying != typeof(int) &&
        underlying != typeof(long) &&
        underlying != typeof(short) &&
        underlying != typeof(byte) &&
        underlying != typeof(double) &&
        underlying != typeof(float))
    {
      throw new ArgumentException($"SumTo '{paramName}' must be a numeric type, got '{type.Name}'.", paramName);
    }
  }
}
