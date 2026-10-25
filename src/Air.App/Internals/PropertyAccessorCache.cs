namespace Platform.App.Internals;

internal static class PropertyAccessorCache
{
  private sealed class TypePropertyComparer : IEqualityComparer<(Type Type, string Name)>
  {
    public static readonly TypePropertyComparer Instance = new();

    public bool Equals((Type Type, string Name) x, (Type Type, string Name) y) =>
      x.Type == y.Type && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

    public int GetHashCode((Type Type, string Name) obj) =>
      HashCode.Combine(obj.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
  }

  private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertiesByType = new();
  private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> PropertyByName = new(TypePropertyComparer.Instance);
  private static readonly ConcurrentDictionary<(Type Type, string Name), Func<object, object?>?> GetterByName = new(TypePropertyComparer.Instance);
  private static readonly ConcurrentDictionary<(Type Type, string Name), Action<object, object?>?> SetterByName = new(TypePropertyComparer.Instance);

  public static PropertyInfo[] GetProperties(Type type) =>
    PropertiesByType.GetOrAdd(type, static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

  public static PropertyInfo? GetProperty(Type type, string name) =>
    PropertyByName.GetOrAdd((type, name), static key =>
      key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

  public static Func<object, object?>? GetGetter(Type type, string name) =>
    GetterByName.GetOrAdd((type, name), static key =>
    {
      var prop = GetProperty(key.Type, key.Name);
      if (prop is null || !prop.CanRead || prop.GetIndexParameters().Length > 0)
      {
        return null;
      }

      var targetParam = Expression.Parameter(typeof(object), "target");
      var typedTarget = Expression.Convert(targetParam, key.Type);
      var propAccess = Expression.Property(typedTarget, prop);
      var boxedValue = prop.PropertyType.IsValueType
        ? Expression.Convert(propAccess, typeof(object))
        : (Expression)propAccess;

      return Expression.Lambda<Func<object, object?>>(boxedValue, targetParam).Compile();
    });

  public static Action<object, object?>? GetSetter(Type type, string name) =>
    SetterByName.GetOrAdd((type, name), static key =>
    {
      var prop = GetProperty(key.Type, key.Name);
      if (prop is null || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
      {
        return null;
      }

      var targetParam = Expression.Parameter(typeof(object), "target");
      var valueParam = Expression.Parameter(typeof(object), "value");
      var typedTarget = Expression.Convert(targetParam, key.Type);
      var typedValue = Expression.Convert(valueParam, prop.PropertyType);
      var assign = Expression.Assign(Expression.Property(typedTarget, prop), typedValue);

      return Expression.Lambda<Action<object, object?>>(assign, targetParam, valueParam).Compile();
    });

  public static object? GetValue(object? target, string propertyName)
  {
    if (target is null || string.IsNullOrEmpty(propertyName))
    {
      return null;
    }

    var getter = GetGetter(target.GetType(), propertyName);
    return getter?.Invoke(target);
  }

  public static object? GetPathValue(object? target, string path)
  {
    if (target is null || string.IsNullOrEmpty(path))
    {
      return null;
    }

    if (!path.Contains('.'))
    {
      if (target is IDictionary<string, object?> d)
      {
        d.TryGetValue(path, out var v);
        return v;
      }

      return GetGetter(target.GetType(), path)?.Invoke(target);
    }

    var current = target;
    foreach (var segment in path.Split('.'))
    {
      if (current is null)
      {
        return null;
      }

      if (current is IDictionary<string, object?> dict)
      {
        dict.TryGetValue(segment, out current);
        continue;
      }

      var getter = GetGetter(current.GetType(), segment);
      if (getter is null)
      {
        return null;
      }

      current = getter(current);
    }

    return current;
  }
}
