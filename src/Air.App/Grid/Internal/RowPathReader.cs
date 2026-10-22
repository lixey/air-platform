namespace Air.App.Grid.Internal;

/// <summary>
/// Reads a dotted column path out of a row, whatever the row is: a typed projection, the entity itself, or the
/// dictionary ShapeRow made of it. A segment walks a public property when the type has one, a key when the
/// type is a dictionary, and a path the static type does not show is walked on the value at run time, reading null
/// where the value has no such member.
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
      var segments = path.Split('.');
      for (var i = 0; i < segments.Length; i++)
      {
        var segment = segments[i];
        var member = current.Type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
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

        if (typeof(IDictionary).IsAssignableFrom(current.Type))
        {
          var value = Expression.Variable(typeof(object), $"entry_{i}");
          variables.Add(value);
          var map = Expression.Convert(current, typeof(IDictionary));
          var contains = typeof(IDictionary).GetMethod(nameof(IDictionary.Contains), [typeof(object)])!;
          var item = typeof(IDictionary).GetMethod("get_Item", [typeof(object)])!;
          var key = Expression.Constant(segment, typeof(object));

          expressions.Add(
            Expression.IfThen(
              Expression.Not(Expression.Call(map, contains, key)),
              Expression.Return(returnTarget, Expression.Constant(null, typeof(object)))
            )
          );
          expressions.Add(Expression.Assign(value, Expression.Call(map, item, key)));
          current = value;
          continue;
        }

        var generic = current.Type.GetInterfaces()
          .Concat([current.Type])
          .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                               t.GetGenericArguments()[0] == typeof(string));
        if (generic is not null)
        {
          var valueType = generic.GetGenericArguments()[1];
          var tryGetValue = generic.GetMethod("TryGetValue")!;
          var value = Expression.Variable(valueType, $"entry_{i}");
          variables.Add(value);
          var key = Expression.Constant(segment, typeof(string));

          expressions.Add(
            Expression.IfThen(
              Expression.Not(Expression.Call(Expression.Convert(current, generic), tryGetValue, key, value)),
              Expression.Return(returnTarget, Expression.Constant(null, typeof(object)))
            )
          );
          current = value;
          continue;
        }

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

  private static object? Walk(object? value, string[] segments)
  {
    var current = value;
    foreach (var segment in segments)
    {
      if (current is null)
      {
        return null;
      }

      if (current is IDictionary<string, object?> map)
      {
        current = map.TryGetValue(segment, out var entry) ? entry : null;
        continue;
      }

      if (current is IDictionary weak)
      {
        current = weak.Contains(segment) ? weak[segment] : null;
        continue;
      }

      current = current.GetType()
        .GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?.GetValue(current);
    }

    return current;
  }
}
