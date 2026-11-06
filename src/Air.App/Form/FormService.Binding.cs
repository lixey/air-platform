using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
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

    var converted = ConvertJsonElementToPropertyValue(prop, val);
    setter(current, converted);
  }

  private static object? ConvertJsonElementToPropertyValue(PropertyInfo prop, JsonElement val)
  {
    var underlying = Nullable.GetUnderlyingType(prop.PropertyType);
    var targetType = underlying ?? prop.PropertyType;

    if (val.ValueKind == JsonValueKind.Object && (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(string)))
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

  private static HashSet<string>? BuildPresentPaths(JsonElement? clientJson)
  {
    if (clientJson is not { ValueKind: JsonValueKind.Object } jsonObj)
    {
      return null;
    }

    var presentInJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var jProp in jsonObj.EnumerateObject())
    {
      if (jProp.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
      {
        var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), jProp.Name);
        if (prop != null && prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
        {
          continue;
        }
      }

      presentInJson.Add(jProp.Name);
    }

    return presentInJson;
  }

  private HashSet<string> BuildStaticProtectedPaths(
    Elements elements,
    HashSet<string>? presentInJson = null,
    bool isNew = true)
  {
    var staticallyEditable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      if (!el.IsDisabled && !el.IsServerOwned && el.Model is not FormInfoModel)
      {
        if (presentInJson == null || presentInJson.Contains(el.Path))
        {
          staticallyEditable.Add(el.Path);
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

    var inputOnlyPaths = BuildInputOnlyPaths(elements);
    foreach (var prop in PropertyAccessorCache.GetProperties(typeof(TEntity)))
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

  private HashSet<string> ComputeDisabledElementPaths(
    Elements elements,
    bool isNew,
    TEntity? storedEntity,
    TEntity? inputEntity,
    HashSet<string>? presentInJson = null)
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
        var staticProtected = BuildStaticProtectedPaths(elements, presentInJson, isNew: true);
        CopyMatchingProperties(inputEntity, effective, staticProtected);
      }

      foreach (var el in elements.List)
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
      foreach (var el in elements.List)
      {
        if (!string.IsNullOrEmpty(el.Path) && IsElementDisabledOnState(el, inputEntity))
        {
          disabledPaths.Add(el.Path);
        }
      }

      return disabledPaths;
    }

    foreach (var el in elements.List)
    {
      if (!string.IsNullOrEmpty(el.Path) && IsElementDisabledOnState(el, storedEntity))
      {
        disabledPaths.Add(el.Path);
      }
    }

    if (inputEntity == null || disabledPaths.Count == 0)
    {
      return disabledPaths;
    }

    var staticProtectedEdit = BuildStaticProtectedPaths(elements, presentInJson, isNew: false);
    var maxIterations = Math.Max(1, elements.List.Count);
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

      var unlockedAny = false;
      foreach (var el in elements.List)
      {
        if (string.IsNullOrEmpty(el.Path) || !disabledPaths.Contains(el.Path))
        {
          continue;
        }

        if (el.IsDisabled)
        {
          continue;
        }

        if (!IsElementDisabledOnState(el, trial))
        {
          disabledPaths.Remove(el.Path);
          unlockedAny = true;
        }
      }

      if (!unlockedAny)
      {
        break;
      }
    }

    return disabledPaths;
  }

  private HashSet<string> BuildProtectedPaths(
    Elements elements,
    HashSet<string>? presentInJson = null,
    bool isNew = true,
    TEntity? entity = null,
    TEntity? input = null,
    HashSet<string>? disabledElementPaths = null)
  {
    disabledElementPaths ??= (entity != null || input != null)
      ? ComputeDisabledElementPaths(elements, isNew, entity, input, presentInJson)
      : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var editablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var isDisabled = el.IsDisabled || disabledElementPaths.Contains(el.Path);
      var acceptsInput = !isDisabled && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (acceptsInput)
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

  private void ApplyBoundParentId(TEntity entity, Parent parent, long? parentId)
  {
    if (!parentId.HasValue ||
        parent.BoundProperty == null ||
        !parent.MatchesService(Request.ParentService) && ResolveParentChildBuilder(requirePreset: true)?.PresetProvider != null)
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
    if (parent.BoundProperty == null ||
        !parent.MatchesService(Request.ParentService) && ResolveParentChildBuilder(requirePreset: true)?.PresetProvider != null)
    {
      return;
    }

    var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
    if (!IsNew && Request.ParentId is > 0 && storedParentId is > 0 && storedParentId.Value != Request.ParentId.Value)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]);
    }

    if (!IsNew && !parent.IsOptional || !Request.ParentId.HasValue)
    {
      if (storedParentId.HasValue)
      {
        Request.ParentId = storedParentId.Value;
      }
    }
  }

  private void ApplyParentChildPreset(TEntity entity, Parent parent)
  {
    if (parent.BoundProperty != null && parent.MatchesService(Request.ParentService) ||
        Request.ParentId is not > 0 ||
        string.IsNullOrEmpty(Request.ParentService))
    {
      return;
    }

    var match = ResolveParentChildBuilder(requirePreset: true);
    if (match?.PresetProvider != null)
    {
      match.PresetProvider(db, Request.ParentId.Value, entity);
    }
  }

  private (string? Service, long? ParentId, IReadOnlyList<ChildGridBuilder> Children)? _cachedParentChildren;

  private Task<ChildGridBuilder?> ResolveParentChildBuilderAsync(bool requirePreset = false, CancellationToken ct = default) =>
    ParentScopeValidator.ResolveChildBuilderAsync(
      db,
      PlatformApp,
      Request.ParentService,
      GetParent().BoundParentService,
      Request.ParentId,
      val => _cachedParentChildren = val,
      _cachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false),
      ct);

  private ChildGridBuilder? ResolveParentChildBuilder(bool requirePreset = false) =>
    ParentScopeValidator.ResolveChildBuilder(
      Request.ParentService,
      GetParent().BoundParentService,
      Request.ParentId,
      _cachedParentChildren,
      c => !c.IsFiles &&
           (!requirePreset || c.PresetProvider != null) &&
           ((c.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(c.ServiceType))?.IsAssignableFrom(typeof(TEntity)) ?? false));
}
