namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  internal static void SetPropertyValue(object target, string path, JsonElement val)
  {
    if (target == null || string.IsNullOrEmpty(path))
    {
      return;
    }

    var current = target;
    var parts = path.Split('.');
    for (var i = 0; i < parts.Length - 1; i++)
    {
      var next = PropertyAccessorCache.GetValue(current, parts[i]);
      if (next == null)
      {
        return;
      }

      current = next;
    }

    var currentType = current.GetType();
    var prop = PropertyAccessorCache.GetProperty(currentType, parts[^1]);
    var setter = PropertyAccessorCache.GetSetter(currentType, parts[^1]);
    if (prop == null || setter == null)
    {
      return;
    }

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
    else
    {
      converted = targetType == typeof(double)
        ? val.ValueKind == JsonValueKind.Number ? val.GetDouble() : double.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0d
        : targetType == typeof(float)
        ? val.ValueKind == JsonValueKind.Number ? val.GetSingle() : float.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var f) ? f : 0f
        : targetType == typeof(int)
        ? val.ValueKind == JsonValueKind.Number ? val.GetInt32() : int.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var i) ? i : 0
        : targetType == typeof(long)
        ? val.ValueKind == JsonValueKind.Number ? val.GetInt64() : long.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var l) ? l : 0L
        : targetType == typeof(short)
        ? val.ValueKind == JsonValueKind.Number ? val.GetInt16() : short.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var s) ? s : (short)0
        : targetType == typeof(bool)
        ? val.ValueKind switch
        {
          JsonValueKind.True => true,
          JsonValueKind.False => false,
          JsonValueKind.String => bool.TryParse(val.GetString(), out var b) && b,
          _ => false
        }
        : targetType == typeof(DateTime)
        ? val.GetDateTime()
        : targetType == typeof(DateOnly)
        ? DateOnly.Parse(val.GetString()!)
        : targetType == typeof(TimeOnly)
        ? TimeOnly.Parse(val.GetString()!)
        : targetType.IsEnum
        ? val.ValueKind == JsonValueKind.Number
        ? Enum.ToObject(targetType, val.GetInt64())
        : Enum.Parse(targetType, val.GetString()!, true)
        : JsonSerializer.Deserialize(val.GetRawText(), prop.PropertyType);
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
      if (!prop.CanRead || IsIgnoredNavigationProperty(prop.PropertyType))
      {
        continue;
      }

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

  private HashSet<string> BuildProtectedPaths(Elements elements, JsonElement? clientJson = null, bool isNew = true)
  {
    HashSet<string>? presentInJson = null;
    if (!isNew && clientJson is { ValueKind: JsonValueKind.Object } jsonObj)
    {
      presentInJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var jProp in jsonObj.EnumerateObject())
      {
        presentInJson.Add(jProp.Name);
      }
    }

    var editablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var isSeparateInput = typeof(TInput) != typeof(TEntity);
    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var acceptsInput = !el.IsDisabled && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (acceptsInput && !(isSeparateInput && el.IsInputOnly))
      {
        if (presentInJson == null || presentInJson.Contains(el.Path))
        {
          editablePaths.Add(el.Path);
        }
      }
    }

    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Id",
      "Ver",
      "CreatedAt",
      "CreatedBy",
      "UpdatedAt",
      "UpdatedBy"
    };

    var parent = GetParent();
    if (!isNew && parent.BoundProperty != null && !parent.IsOptional)
    {
      set.Add(parent.BoundProperty.Name);
    }

    foreach (var prop in PropertyAccessorCache.GetProperties(typeof(TEntity)))
    {
      if (!editablePaths.Contains(prop.Name) || prop.GetCustomAttribute<SensitiveAttribute>() != null)
      {
        set.Add(prop.Name);
      }
    }

    return set;
  }

  private static HashSet<string> BuildInputOnlyPaths(Elements elements)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements.List)
    {
      if (el.IsInputOnly && !string.IsNullOrEmpty(el.Path))
      {
        set.Add(el.Path);
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
      if (!sp.CanRead || protectedPaths != null && protectedPaths.Contains(sp.Name))
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
    if (typeof(IEntity).IsAssignableFrom(propertyType))
    {
      return true;
    }

    if (propertyType == typeof(string) || propertyType == typeof(byte[]))
    {
      return false;
    }

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

  private static void NormalizeNullableStrings(Elements elements, object target)
  {
    var targetType = target.GetType();
    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var prop = PropertyAccessorCache.GetProperty(targetType, el.Path);
      if (prop == null || prop.PropertyType != typeof(string) || !prop.CanRead || !prop.CanWrite)
      {
        continue;
      }

      if (PropertyAccessorCache.GetValue(target, prop.Name) is not string str)
      {
        continue;
      }

      if (el.Constraints.AllowsNull == true && el.Constraints.RejectsBlank != true && string.IsNullOrWhiteSpace(str))
      {
        prop.SetValue(target, null);
        continue;
      }

      if (el.Model is FormPhoneModel && str.Length > 0 && str.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))
      {
        var digits = new string(str.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length > 0)
        {
          prop.SetValue(target, "+" + digits);
        }
      }
    }
  }

  private static void ApplyBoundParentId(TEntity entity, Parent parent, long? parentId)
  {
    if (!parentId.HasValue || parent.BoundProperty == null)
    {
      return;
    }

    if (parent.BoundProperty.PropertyType == typeof(long))
    {
      parent.BoundProperty.SetValue(entity, parentId.Value);
    }
    else if (parent.BoundProperty.PropertyType == typeof(long?))
    {
      parent.BoundProperty.SetValue(entity, (long?)parentId.Value);
    }
  }

  private void RestoreBoundParentId(TEntity entity, Parent parent)
  {
    if (parent.BoundProperty == null)
    {
      return;
    }

    if (!IsNew && !parent.IsOptional || !Request.ParentId.HasValue)
    {
      var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
      if (storedParentId.HasValue)
      {
        Request.ParentId = storedParentId.Value;
      }
    }
  }
}
