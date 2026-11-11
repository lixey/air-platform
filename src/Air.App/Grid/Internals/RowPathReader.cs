using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

/// <summary>
/// Reads a dotted column path out of a row, whatever the row is: a <see cref="RowDto"/>, a dictionary, a typed
/// projection, or an entity. Dictionary keys and <see cref="RowDto.Cells"/> are always inspected before CLR properties
/// so column names like <c>count</c>, <c>item</c>, <c>keys</c>, or <c>values</c> never collide with dictionary members.
/// </summary>
internal static class RowPathReader
{
  /// <summary>Compiles a reader of path over rowType; null for an empty path.</summary>
  public static Func<object, object?>? Compile(Type rowType, string path)
  {
    ArgumentNullException.ThrowIfNull(rowType);
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    var segments = path.Split('.');
    if (rowType == typeof(RowDto))
    {
      return row => ReadRowDto((RowDto)row, segments);
    }

    try
    {
      var parameter = Expression.Parameter(typeof(object), "row");
      var returnTarget = Expression.Label(typeof(object));
      var variables = new List<ParameterExpression>();
      var expressions = new List<Expression>();

      var typed = Expression.Variable(rowType, "typed");
      variables.Add(typed);
      expressions.Add(Expression.Assign(typed, Expression.Convert(parameter, rowType)));

      Expression current = typed;
      for (var i = 0; i < segments.Length; i++)
      {
        var segment = segments[i];
        var generic = current.Type.GetInterfaces()
          .Concat([current.Type])
          .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                               t.GetGenericArguments()[0] == typeof(string));
        if (generic is not null || typeof(IDictionary).IsAssignableFrom(current.Type))
        {
          var rest = segments[i..];
          var walker = Expression.Call(
            typeof(RowPathReader).GetMethod(nameof(Walk), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(current, typeof(object)),
            Expression.Constant(rest)
          );
          expressions.Add(Expression.Return(returnTarget, walker));
          expressions.Add(Expression.Label(returnTarget, Expression.Constant(null, typeof(object))));
          return Expression.Lambda<Func<object, object?>>(Expression.Block(variables, expressions), parameter).Compile();
        }

        var member = FindScalarProperty(current.Type, segment);
        if (member is not null)
        {
          var step = Expression.Variable(member.PropertyType, $"step_{i}");
          variables.Add(step);
          expressions.Add(Expression.Assign(step, Expression.Property(current, member)));
          if (!member.PropertyType.IsValueType || Nullable.GetUnderlyingType(member.PropertyType) is not null)
          {
            expressions.Add(
              Expression.IfThen(
                Expression.Equal(step, Expression.Constant(null, member.PropertyType)),
                Expression.Return(returnTarget, Expression.Constant(null, typeof(object)))
              )
            );
          }

          current = step;
          continue;
        }

        var remaining = segments[i..];
        var dynamicWalker = Expression.Call(
          typeof(RowPathReader).GetMethod(nameof(Walk), BindingFlags.NonPublic | BindingFlags.Static)!,
          Expression.Convert(current, typeof(object)),
          Expression.Constant(remaining)
        );
        expressions.Add(Expression.Return(returnTarget, dynamicWalker));
        expressions.Add(Expression.Label(returnTarget, Expression.Constant(null, typeof(object))));
        return Expression.Lambda<Func<object, object?>>(Expression.Block(variables, expressions), parameter).Compile();
      }

      expressions.Add(Expression.Return(returnTarget, Expression.Convert(current, typeof(object))));
      expressions.Add(Expression.Label(returnTarget, Expression.Constant(null, typeof(object))));

      return Expression.Lambda<Func<object, object?>>(Expression.Block(variables, expressions), parameter).Compile();
    }
    catch (Exception error) when (error is AmbiguousMatchException or ArgumentException)
    {
      throw new InvalidOperationException(
        $"Column path '{path}' cannot be read from row '{rowType.Name}': a segment names two members that differ by case, " +
        "or an indexer; name the member exactly.",
        error
      );
    }
  }

  private static object? ReadRowDto(RowDto row, string[] segments)
  {
    if (segments.Length == 0)
    {
      return null;
    }

    var first = segments[0];
    if (!TryGetMapEntry(row.Cells, first, out var current))
    {
      if (segments.Length == 1)
      {
        if (string.Equals(first, "id", StringComparison.OrdinalIgnoreCase))
        {
          return row.Id;
        }

        if (string.Equals(first, "ver", StringComparison.OrdinalIgnoreCase))
        {
          return row.Ver;
        }
      }

      return null;
    }

    return segments.Length == 1 ? current : Walk(current, segments[1..]);
  }

  private static object? Walk(object? value, string[] segments)
  {
    var current = value;
    foreach (var segment in segments)
    {
      if (current is null)
      {
        return null;
      }

      if (current is RowDto rowDto)
      {
        current = ReadRowDto(rowDto, [segment]);
        continue;
      }

      if (current is IDictionary<string, object?> map)
      {
        current = TryGetMapEntry(map, segment, out var entry) ? entry : null;
        continue;
      }

      if (current is IDictionary weak)
      {
        current = TryGetWeakEntry(weak, segment, out var entry) ? entry : null;
        continue;
      }

      current = PropertyAccessorCache.GetGetter(current.GetType(), segment)?.Invoke(current);
    }

    return current;
  }

  private static bool TryGetMapEntry(IDictionary<string, object?> map, string key, out object? value)
  {
    if (map.TryGetValue(key, out value))
    {
      return true;
    }

    var camel = PropertyAccessorCache.ToCamel(key);
    if (!string.Equals(camel, key, StringComparison.Ordinal) && map.TryGetValue(camel, out value))
    {
      return true;
    }

    if (camel.Length > 0)
    {
      var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
      if (!string.Equals(pascal, key, StringComparison.Ordinal) && map.TryGetValue(pascal, out value))
      {
        return true;
      }
    }

    value = null;
    return false;
  }

  private static bool TryGetWeakEntry(IDictionary map, string key, out object? value)
  {
    if (map.Contains(key))
    {
      value = map[key];
      return true;
    }

    var camel = PropertyAccessorCache.ToCamel(key);
    if (!string.Equals(camel, key, StringComparison.Ordinal) && map.Contains(camel))
    {
      value = map[camel];
      return true;
    }

    if (camel.Length > 0)
    {
      var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
      if (!string.Equals(pascal, key, StringComparison.Ordinal) && map.Contains(pascal))
      {
        value = map[pascal];
        return true;
      }
    }

    value = null;
    return false;
  }

  private static PropertyInfo? FindScalarProperty(Type type, string name)
  {
    var properties = PropertyAccessorCache.GetProperties(type);
    PropertyInfo? exact = null;
    for (var i = 0; i < properties.Length; i++)
    {
      var p = properties[i];
      if (p.GetIndexParameters().Length == 0 && string.Equals(p.Name, name, StringComparison.Ordinal))
      {
        if (exact is not null)
        {
          throw new AmbiguousMatchException($"Multiple properties match '{name}' on '{type.Name}'.");
        }

        exact = p;
      }
    }

    if (exact is not null)
    {
      return exact;
    }

    PropertyInfo? ciMatch = null;
    for (var i = 0; i < properties.Length; i++)
    {
      var p = properties[i];
      if (p.GetIndexParameters().Length == 0 && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        if (ciMatch is not null)
        {
          throw new AmbiguousMatchException($"Multiple properties match '{name}' on '{type.Name}'.");
        }

        ciMatch = p;
      }
    }

    return ciMatch;
  }
}
