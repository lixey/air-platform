using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Air.App.Internal;
using Air.Data.Entities;

namespace Air.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  internal static void SetPropertyValue(object target, string path, JsonElement val)
  {
    if (target == null || string.IsNullOrEmpty(path)) return;
    var current = target;
    var parts = path.Split('.');
    for (var i = 0; i < parts.Length - 1; i++)
    {
      var next = PropertyAccessorCache.GetValue(current, parts[i]);
      if (next == null) return;
      current = next;
    }

    var currentType = current.GetType();
    var prop = PropertyAccessorCache.GetProperty(currentType, parts[^1]);
    var setter = PropertyAccessorCache.GetSetter(currentType, parts[^1]);
    if (prop == null || setter == null) return;

    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
    object? converted;

    if (val.ValueKind == JsonValueKind.Object && (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(string)))
    {
      if (val.TryGetProperty("id", out var idElem))
      {
        val = idElem;
      }
    }

    if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
      converted = null;
    }
    else if (targetType == typeof(string))
    {
      converted = val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText();
    }
    else if (targetType == typeof(decimal))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetDecimal() : decimal.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
    else if (targetType == typeof(double))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetDouble() : double.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0d;
    }
    else if (targetType == typeof(float))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetSingle() : float.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var f) ? f : 0f;
    }
    else if (targetType == typeof(int))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt32() : int.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var i) ? i : 0;
    }
    else if (targetType == typeof(long))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt64() : long.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var l) ? l : 0L;
    }
    else if (targetType == typeof(short))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt16() : short.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var s) ? s : (short)0;
    }
    else if (targetType == typeof(bool))
    {
      converted = val.ValueKind switch
      {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(val.GetString(), out var b) && b,
        _ => false
      };
    }
    else if (targetType == typeof(DateTime))
    {
      converted = val.GetDateTime();
    }
    else if (targetType == typeof(DateOnly))
    {
      converted = DateOnly.Parse(val.GetString()!);
    }
    else if (targetType == typeof(TimeOnly))
    {
      converted = TimeOnly.Parse(val.GetString()!);
    }
    else if (targetType.IsEnum)
    {
      converted = val.ValueKind == JsonValueKind.Number
        ? Enum.ToObject(targetType, val.GetInt64())
        : Enum.Parse(targetType, val.GetString()!, true);
    }
    else
    {
      converted = JsonSerializer.Deserialize(val.GetRawText(), prop.PropertyType);
    }

    setter(current, converted);
  }

  private static Dictionary<string, object?> SnapshotValues(object entity)
  {
    var entityType = entity.GetType();
    var props = PropertyAccessorCache.GetProperties(entityType);
    var dict = new Dictionary<string, object?>(props.Length, StringComparer.OrdinalIgnoreCase);

    foreach (var prop in props)
    {
      if (!prop.CanRead || IsIgnoredNavigationProperty(prop.PropertyType)) continue;
      try
      {
        var val = PropertyAccessorCache.GetValue(entity, prop.Name);
        if (val is IEnumerable enumerable and not string and not byte[])
        {
          val = enumerable.Cast<object?>().ToArray();
        }
        dict[prop.Name] = val;
      }
      catch { }
    }
    return dict;
  }

  private static HashSet<string> BuildProtectedPaths(Elements elements)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Id",
      "Ver",
      "CreatedAt",
      "CreatedBy",
      "UpdatedAt",
      "UpdatedBy"
    };

    var isSeparateInput = typeof(TInput) != typeof(TEntity);
    foreach (var el in elements.List)
    {
      if ((el.IsServerOwned || (isSeparateInput && el.IsInputOnly)) && !string.IsNullOrEmpty(el.Path))
      {
        set.Add(el.Path);
      }
    }

    foreach (var prop in PropertyAccessorCache.GetProperties(typeof(TEntity)))
    {
      if (prop.GetCustomAttribute<SensitiveAttribute>() != null)
      {
        set.Add(prop.Name);
      }
    }

    return set;
  }

  private static void CopyMatchingProperties(object source, object target, HashSet<string>? protectedPaths = null)
  {
    var srcType = source.GetType();
    var tgtType = target.GetType();

    foreach (var sp in PropertyAccessorCache.GetProperties(srcType))
    {
      if (!sp.CanRead || (protectedPaths != null && protectedPaths.Contains(sp.Name)))
      {
        continue;
      }

      var tp = PropertyAccessorCache.GetProperty(tgtType, sp.Name);
      if (tp == null || !tp.CanWrite || !tp.PropertyType.IsAssignableFrom(sp.PropertyType))
      {
        continue;
      }

      if (IsIgnoredNavigationProperty(tp.PropertyType))
      {
        continue;
      }

      try
      {
        var getter = PropertyAccessorCache.GetGetter(srcType, sp.Name);
        var setter = PropertyAccessorCache.GetSetter(tgtType, tp.Name);
        if (getter != null && setter != null)
        {
          setter(target, getter(source));
        }
      }
      catch { }
    }
  }

  private static bool IsIgnoredNavigationProperty(Type propertyType)
  {
    if (typeof(IEntity).IsAssignableFrom(propertyType)) return true;
    if (propertyType == typeof(string) || propertyType == typeof(byte[])) return false;
    if (typeof(IEnumerable).IsAssignableFrom(propertyType))
    {
      var elemType = propertyType.IsArray
        ? propertyType.GetElementType()
        : propertyType.IsGenericType
          ? propertyType.GetGenericArguments()[0]
          : null;
      return elemType == null || typeof(IEntity).IsAssignableFrom(elemType);
    }
    return false;
  }
}
