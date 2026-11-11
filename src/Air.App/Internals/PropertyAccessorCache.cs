namespace Platform.App.Internals;

internal static class PropertyAccessorCache
{
  private const int MaxStringCacheEntries = 4096;
  private const int MaxCachedPathLength = 128;

  private static readonly string[] LabelCandidateNames = ["Name", "Title", "Number", "Code", "Login"];

  private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertiesByType = new();
  private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, CompiledPropertyAccessor>> AccessorsByType = new();
  private static readonly ConcurrentDictionary<Type, Func<object, object?>?> LabelGetterByType = new();
  private static readonly ConcurrentDictionary<string, string[]> PathSegmentsCache = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, string[]> CamelPathSegmentsCache = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, string> CamelCaseCache = new(StringComparer.Ordinal);

  public static PropertyInfo[] GetProperties(Type type) =>
    PropertiesByType.GetOrAdd(type, static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

  public static PropertyInfo? GetProperty(Type type, string name) =>
    !string.IsNullOrEmpty(name) && GetAccessors(type).TryGetValue(name, out var accessor)
      ? accessor.Property
      : null;

  public static Func<object, object?>? GetGetter(Type type, string name) =>
    !string.IsNullOrEmpty(name) && GetAccessors(type).TryGetValue(name, out var accessor)
      ? accessor.Getter
      : null;

  public static Action<object, object?>? GetSetter(Type type, string name) =>
    !string.IsNullOrEmpty(name) && GetAccessors(type).TryGetValue(name, out var accessor)
      ? accessor.Setter
      : null;

  public static string? GetReferenceLabel(object target)
  {
    var getter = LabelGetterByType.GetOrAdd(target.GetType(), static type =>
    {
      foreach (var candidate in LabelCandidateNames)
      {
        var g = GetGetter(type, candidate);
        if (g is not null)
        {
          return g;
        }
      }

      return null;
    });

    return getter?.Invoke(target)?.ToString();
  }

  public static string[] GetPathSegments(string path)
  {
    if (PathSegmentsCache.TryGetValue(path, out var cached))
    {
      return cached;
    }

    var computed = path.Split('.');
    if (path.Length <= MaxCachedPathLength && PathSegmentsCache.Count < MaxStringCacheEntries)
    {
      PathSegmentsCache.TryAdd(path, computed);
    }

    return computed;
  }

  public static string[] GetCamelPathSegments(string path)
  {
    if (CamelPathSegmentsCache.TryGetValue(path, out var cached))
    {
      return cached;
    }

    var computed = BuildCamelPathSegments(path);
    if (path.Length <= MaxCachedPathLength && CamelPathSegmentsCache.Count < MaxStringCacheEntries)
    {
      CamelPathSegmentsCache.TryAdd(path, computed);
    }

    return computed;
  }

  public static string ToCamel(string str)
  {
    if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
    {
      return str;
    }

    if (CamelCaseCache.TryGetValue(str, out var cached))
    {
      return cached;
    }

    var computed = char.ToLowerInvariant(str[0]) + str[1..];
    if (str.Length <= MaxCachedPathLength && CamelCaseCache.Count < MaxStringCacheEntries)
    {
      CamelCaseCache.TryAdd(str, computed);
    }

    return computed;
  }

  public static object? GetValue(object? target, string propertyName)
  {
    if (target is null || string.IsNullOrEmpty(propertyName))
    {
      return null;
    }

    return GetGetter(target.GetType(), propertyName)?.Invoke(target);
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
        return TryGetDictionaryValue(d, path);
      }

      return GetGetter(target.GetType(), path)?.Invoke(target);
    }

    var current = target;
    var segments = GetPathSegments(path);
    for (var i = 0; i < segments.Length; i++)
    {
      if (current is null)
      {
        return null;
      }

      var segment = segments[i];
      if (current is IDictionary<string, object?> dict)
      {
        current = TryGetDictionaryValue(dict, segment);
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

  private static FrozenDictionary<string, CompiledPropertyAccessor> GetAccessors(Type type) =>
    AccessorsByType.GetOrAdd(type, BuildAccessors);

  private static FrozenDictionary<string, CompiledPropertyAccessor> BuildAccessors(Type type)
  {
    var props = GetProperties(type);
    var map = new Dictionary<string, CompiledPropertyAccessor>(props.Length, StringComparer.OrdinalIgnoreCase);
    foreach (var prop in props)
    {
      if (prop.GetIndexParameters().Length > 0 || map.ContainsKey(prop.Name))
      {
        continue;
      }

      map[prop.Name] = new CompiledPropertyAccessor(
        prop,
        CompileGetter(type, prop),
        CompileSetter(type, prop));
    }

    return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
  }

  private static Func<object, object?>? CompileGetter(Type type, PropertyInfo prop)
  {
    if (!prop.CanRead)
    {
      return null;
    }

    var targetParam = Expression.Parameter(typeof(object), "target");
    var typedTarget = Expression.Convert(targetParam, type);
    var propAccess = Expression.Property(typedTarget, prop);
    var boxedValue = prop.PropertyType.IsValueType
      ? Expression.Convert(propAccess, typeof(object))
      : (Expression)propAccess;

    return Expression.Lambda<Func<object, object?>>(boxedValue, targetParam).Compile();
  }

  private static Action<object, object?>? CompileSetter(Type type, PropertyInfo prop)
  {
    if (!prop.CanWrite)
    {
      return null;
    }

    var targetParam = Expression.Parameter(typeof(object), "target");
    var valueParam = Expression.Parameter(typeof(object), "value");
    var typedTarget = Expression.Convert(targetParam, type);
    var typedValue = Expression.Convert(valueParam, prop.PropertyType);
    var assign = Expression.Assign(Expression.Property(typedTarget, prop), typedValue);

    return Expression.Lambda<Action<object, object?>>(assign, targetParam, valueParam).Compile();
  }

  private static string[] BuildCamelPathSegments(string path)
  {
    var raw = GetPathSegments(path);
    var res = new string[raw.Length];
    for (var i = 0; i < raw.Length; i++)
    {
      res[i] = ToCamel(raw[i]);
    }

    return res;
  }

  private static object? TryGetDictionaryValue(IDictionary<string, object?> dict, string key)
  {
    if (dict.TryGetValue(key, out var exact))
    {
      return exact;
    }

    foreach (var kv in dict)
    {
      if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
      {
        return kv.Value;
      }
    }

    return null;
  }
}
