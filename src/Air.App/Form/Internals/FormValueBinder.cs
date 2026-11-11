using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormValueBinder
{
  public static void SetPropertyValue(object? target, string? path, JsonElement val)
  {
    if (target == null || string.IsNullOrEmpty(path) || path.Contains('.'))
    {
      return;
    }

    var currentType = target.GetType();
    var prop = PropertyAccessorCache.GetProperty(currentType, path);
    var setter = PropertyAccessorCache.GetSetter(currentType, path);
    if (prop == null || setter == null || IsIgnoredNavigationProperty(prop.PropertyType))
    {
      return;
    }

    var converted = ConvertJsonElementToPropertyValue(prop, val);
    setter(target, converted);
  }

  public static TEntity DeserializeInput<TEntity>(JsonElement entityJson)
    where TEntity : class, IEntity, new()
  {
    if (entityJson.ValueKind == JsonValueKind.Object)
    {
      var result = new TEntity();
      foreach (var jProp in entityJson.EnumerateObject())
      {
        if (jProp.Name.Contains('.'))
        {
          continue;
        }

        var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), jProp.Name);
        var setter = PropertyAccessorCache.GetSetter(typeof(TEntity), jProp.Name);
        if (prop == null || setter == null || IsIgnoredNavigationProperty(prop.PropertyType))
        {
          continue;
        }

        var val = jProp.Value;
        var underlying = Nullable.GetUnderlyingType(prop.PropertyType);
        var targetType = underlying ?? prop.PropertyType;
        if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            val.ValueKind == JsonValueKind.String && targetType != typeof(string) && string.IsNullOrWhiteSpace(val.GetString()))
        {
          if (!prop.PropertyType.IsValueType || underlying != null)
          {
            setter(result, null);
          }

          continue;
        }

        SetPropertyValue(result, prop.Name, val);
      }

      return result;
    }

    return JsonSerializer.Deserialize<TEntity>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TEntity();
  }

  public static Dictionary<string, object?> SnapshotValues(object entity)
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
      catch
      {
      }
    }

    return dict;
  }

  public static HashSet<string>? BuildPresentPaths(Type entityType, JsonElement? clientJson)
  {
    if (clientJson is not { ValueKind: JsonValueKind.Object } jsonObj)
    {
      return null;
    }

    var presentInJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var jProp in jsonObj.EnumerateObject())
    {
      if (jProp.Name.Contains('.'))
      {
        continue;
      }

      if (jProp.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
      {
        var prop = PropertyAccessorCache.GetProperty(entityType, jProp.Name);
        if (prop != null && prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
        {
          continue;
        }
      }

      presentInJson.Add(jProp.Name);
    }

    return presentInJson;
  }

  public static HashSet<string> BuildStaticProtectedPaths(
    Type entityType,
    IReadOnlyList<FormElementDto> elements,
    PropertyInfo? boundProperty,
    bool isParentOptional,
    HashSet<string>? presentInJson = null,
    bool isNew = true)
  {
    var staticallyEditable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.Path) || el.Path.Contains('.'))
      {
        continue;
      }

      if (!el.IsHidden && !el.IsDisabled && !el.IsServerOwned && el.Model is not FormInfoModel)
      {
        if (presentInJson == null || presentInJson.Contains(el.Path))
        {
          staticallyEditable.Add(el.Path);
        }
      }
    }

    var set = CreateSystemProtectedSet(boundProperty, isParentOptional, isNew);
    var inputOnlyPaths = BuildInputOnlyPaths(elements);
    foreach (var prop in PropertyAccessorCache.GetProperties(entityType))
    {
      if (!staticallyEditable.Contains(prop.Name))
      {
        set.Add(prop.Name);
        continue;
      }

      var isSensitive = prop.GetCustomAttribute<SensitiveAttribute>() != null;
      var isInputOnly = inputOnlyPaths.Contains(prop.Name);
      if (isSensitive && !isInputOnly)
      {
        set.Add(prop.Name);
      }
    }

    return set;
  }

  public static HashSet<string> ComputeDisabledElementPaths<TEntity>(
    IReadOnlyList<FormElementDto> elements,
    PropertyInfo? boundProperty,
    bool isParentOptional,
    bool isNew,
    TEntity? storedEntity,
    TEntity? inputEntity,
    HashSet<string>? presentInJson = null)
    where TEntity : class, IEntity, new()
  {
    var disabledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (isNew)
    {
      var effective = new TEntity();
      if (storedEntity != null)
      {
        CopyMatchingProperties(storedEntity, effective);
      }

      if (inputEntity != null)
      {
        var staticProtected = BuildStaticProtectedPaths(typeof(TEntity), elements, boundProperty, isParentOptional, presentInJson, isNew: true);
        CopyMatchingProperties(inputEntity, effective, staticProtected);
      }

      foreach (var el in elements)
      {
        if (!string.IsNullOrEmpty(el.Path) && IsElementDisabledOnState(el, effective))
        {
          disabledPaths.Add(el.Path);
        }
      }

      return disabledPaths;
    }

    if (storedEntity == null)
    {
      foreach (var el in elements)
      {
        if (!string.IsNullOrEmpty(el.Path) && IsElementDisabledOnState(el, inputEntity))
        {
          disabledPaths.Add(el.Path);
        }
      }

      return disabledPaths;
    }

    foreach (var el in elements)
    {
      if (!string.IsNullOrEmpty(el.Path) && IsElementDisabledOnState(el, storedEntity))
      {
        disabledPaths.Add(el.Path);
      }
    }

    if (inputEntity == null || !elements.Any(e => e.DisabledWhen != null))
    {
      return disabledPaths;
    }

    var staticProtectedEdit = BuildStaticProtectedPaths(typeof(TEntity), elements, boundProperty, isParentOptional, presentInJson, isNew: false);
    var maxIterations = Math.Max(1, elements.Count);
    for (var iter = 0; iter < maxIterations; iter++)
    {
      var trial = new TEntity();
      CopyMatchingProperties(storedEntity, trial);
      trial.Id = storedEntity.Id;

      var currentProtected = new HashSet<string>(staticProtectedEdit, StringComparer.OrdinalIgnoreCase);
      foreach (var dp in disabledPaths)
      {
        currentProtected.Add(dp);
      }

      CopyMatchingProperties(inputEntity, trial, currentProtected);

      var changedAny = false;
      foreach (var el in elements)
      {
        if (string.IsNullOrEmpty(el.Path) || el.IsDisabled || el.DisabledWhen == null)
        {
          continue;
        }

        var disabledNow = IsElementDisabledOnState(el, trial);
        if (disabledNow ? disabledPaths.Add(el.Path) : disabledPaths.Remove(el.Path))
        {
          changedAny = true;
        }
      }

      if (!changedAny)
      {
        break;
      }
    }

    return disabledPaths;
  }

  public static HashSet<string> BuildProtectedPaths<TEntity>(
    IReadOnlyList<FormElementDto> elements,
    PropertyInfo? boundProperty,
    bool isParentOptional,
    HashSet<string>? presentInJson = null,
    bool isNew = true,
    TEntity? entity = null,
    TEntity? input = null,
    HashSet<string>? disabledElementPaths = null)
    where TEntity : class, IEntity, new()
  {
    disabledElementPaths ??= (entity != null || input != null)
      ? ComputeDisabledElementPaths(elements, boundProperty, isParentOptional, isNew, entity, input, presentInJson)
      : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var editablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.Path) || el.Path.Contains('.'))
      {
        continue;
      }

      var isDisabled = el.IsDisabled || disabledElementPaths.Contains(el.Path);
      var acceptsInput = !el.IsHidden && !isDisabled && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (acceptsInput && (presentInJson == null || presentInJson.Contains(el.Path)))
      {
        editablePaths.Add(el.Path);
      }
    }

    var set = CreateSystemProtectedSet(boundProperty, isParentOptional, isNew);
    var inputOnlyPaths = BuildInputOnlyPaths(elements);
    foreach (var prop in PropertyAccessorCache.GetProperties(typeof(TEntity)))
    {
      if (!editablePaths.Contains(prop.Name))
      {
        set.Add(prop.Name);
        continue;
      }

      var isSensitive = prop.GetCustomAttribute<SensitiveAttribute>() != null;
      var isInputOnly = inputOnlyPaths.Contains(prop.Name);
      if (isSensitive && !isInputOnly)
      {
        set.Add(prop.Name);
        continue;
      }

      if (isSensitive && !isNew && input != null)
      {
        var incoming = PropertyAccessorCache.GetValue(input, prop.Name);
        if (incoming is null || incoming is string s && string.IsNullOrEmpty(s))
        {
          set.Add(prop.Name);
        }
      }
    }

    return set;
  }

  public static bool IsElementDisabledOnState(FormElementDto el, object? state)
  {
    if (el.IsDisabled)
    {
      return true;
    }

    if (state == null || el.DisabledWhen == null)
    {
      return false;
    }

    return FilterNodeEvaluator.Evaluate(el.DisabledWhen, state);
  }

  public static bool IsElementHidden(FormElementDto el, object? state)
  {
    if (el.IsHidden)
    {
      return true;
    }

    if (state == null || el.VisibleWhen == null)
    {
      return false;
    }

    return !FilterNodeEvaluator.Evaluate(el.VisibleWhen, state);
  }

  public static void CopyMatchingProperties(object source, object target, HashSet<string>? protectedPaths = null)
  {
    if (ReferenceEquals(source, target))
    {
      return;
    }

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
      catch
      {
      }
    }
  }

  public static bool IsIgnoredNavigationProperty(Type propertyType)
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

  public static void NormalizeNullableStrings(IReadOnlyList<FormElementDto> elements, object target)
  {
    var targetType = target.GetType();
    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.Path) || el.Path.Contains('.'))
      {
        continue;
      }

      var prop = PropertyAccessorCache.GetProperty(targetType, el.Path);
      var setter = PropertyAccessorCache.GetSetter(targetType, el.Path);
      if (prop == null || setter == null || prop.PropertyType != typeof(string) || !prop.CanRead || !prop.CanWrite)
      {
        continue;
      }

      if (PropertyAccessorCache.GetValue(target, prop.Name) is not string str)
      {
        continue;
      }

      if (el.Constraints.AllowsNull == true && el.Constraints.RejectsBlank != true && string.IsNullOrWhiteSpace(str))
      {
        setter(target, null);
        continue;
      }

      if (el.Model is FormPhoneModel && str.Length > 0 && str.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))
      {
        var digits = new string(str.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length > 0)
        {
          setter(target, "+" + digits);
        }
      }
    }
  }

  private static HashSet<string> CreateSystemProtectedSet(PropertyInfo? boundProperty, bool isParentOptional, bool isNew)
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

    if (!isNew && boundProperty != null && !isParentOptional)
    {
      set.Add(boundProperty.Name);
    }

    return set;
  }

  private static HashSet<string> BuildInputOnlyPaths(IReadOnlyList<FormElementDto> elements)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements)
    {
      if (el.IsInputOnly && !string.IsNullOrEmpty(el.Path))
      {
        set.Add(el.Path);
      }
    }

    return set;
  }

  private static object? ConvertJsonElementToPropertyValue(PropertyInfo prop, JsonElement val)
  {
    var underlying = Nullable.GetUnderlyingType(prop.PropertyType);
    var targetType = underlying ?? prop.PropertyType;

    if (val.ValueKind == JsonValueKind.Object &&
        (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(short) || targetType == typeof(byte) || targetType == typeof(string)))
    {
      if (val.TryGetProperty("id", out var idElem) || val.TryGetProperty("Id", out idElem))
      {
        val = idElem;
      }
    }

    if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
        val.ValueKind == JsonValueKind.String && targetType != typeof(string) && string.IsNullOrWhiteSpace(val.GetString()))
    {
      return prop.PropertyType.IsValueType && underlying == null
        ? Activator.CreateInstance(prop.PropertyType)
        : null;
    }

    if (targetType == typeof(string))
    {
      return val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText();
    }

    if (targetType == typeof(bool))
    {
      return val.ValueKind switch
      {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(val.GetString(), out var b) && b,
        JsonValueKind.Number => val.TryGetInt64(out var n) && n != 0,
        _ => throw new JsonException($"Invalid bool value '{val.GetRawText()}'.")
      };
    }

    if (targetType == typeof(DateOnly))
    {
      return DateOnly.TryParse(val.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
        ? parsedDate
        : DateTimeOffset.TryParse(val.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtoForDate)
          ? DateOnly.FromDateTime(dtoForDate.DateTime)
          : DateTime.TryParse(val.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtForDate)
            ? DateOnly.FromDateTime(dtForDate)
            : throw new JsonException($"Invalid DateOnly value '{val.GetRawText()}'.");
    }

    if (targetType == typeof(TimeOnly))
    {
      return TimeOnly.TryParse(val.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
        ? parsedTime
        : throw new JsonException($"Invalid TimeOnly value '{val.GetRawText()}'.");
    }

    return targetType.IsEnum
      ? val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var enumNum)
        ? Enum.ToObject(targetType, enumNum)
        : Enum.TryParse(targetType, val.GetString(), true, out var parsedEnum)
          ? parsedEnum
          : throw new JsonException($"Invalid {targetType.Name} value '{val.GetRawText()}'.")
      : JsonSerializer.Deserialize(val.GetRawText(), prop.PropertyType, PlatformJsonOptions.DefaultOptions);
  }
}
