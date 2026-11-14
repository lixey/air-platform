using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormValidator
{
  public static void ValidateElementConstraints<TEntity>(
    PlatformDbContext? db,
    Localizer l,
    IReadOnlyList<FormElementDto> elements,
    TEntity entity,
    RuleContext<TEntity> ruleCtx,
    bool onlyAcceptingInput,
    IReadOnlySet<string>? disabledElementPaths = null)
    where TEntity : class
  {
    var efEntityType = db?.Model.FindEntityType(typeof(TEntity));

    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.Path) || el.Path.Contains('.'))
      {
        continue;
      }

      var isHiddenNow = FormValueBinder.IsElementHidden(el, entity);
      var isDisabledOnEffective = FormValueBinder.IsElementDisabledOnState(el, entity);
      var isDisabledForInput = disabledElementPaths != null
        ? el.IsDisabled || disabledElementPaths.Contains(el.Path)
        : isDisabledOnEffective;
      var acceptsInput = !el.IsHidden && !isDisabledForInput && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (onlyAcceptingInput)
      {
        if (!acceptsInput)
        {
          continue;
        }
      }
      else
      {
        if (acceptsInput || el.IsInputOnly || !el.IsRequired || isHiddenNow || isDisabledOnEffective)
        {
          continue;
        }
      }

      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp == null)
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(entity, entityProp.Name);
      var efProp = efEntityType != null && !el.IsInputOnly ? efEntityType.FindProperty(entityProp.Name) : null;
      var isFk = el.ValueKind == FormValueKind.Reference || !string.IsNullOrEmpty(el.ReferencePath) || (efProp?.IsForeignKey() ?? false);
      var isRequiredNow = !isHiddenNow && !isDisabledOnEffective && el.IsRequired;

      if (isRequiredNow && !AnswersRequirement(val, isFk, el.Model))
      {
        AddRequiredFailure(ruleCtx, l, el.Path);
        continue;
      }

      if (!onlyAcceptingInput)
      {
        continue;
      }

      if (val is null)
      {
        if (el.Constraints.AllowsNull == false && !el.IsRequired && !isHiddenNow)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationNull"],
            Code = "form.validation.null",
            Type = MessageType.Error
          });
        }

        continue;
      }

      if (val is string text && text.Length > 0)
      {
        if (el.Constraints.MaxLength is { } maxLen && text.Length > maxLen)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationMaxLength"],
            Code = "form.validation.maxLength",
            Type = MessageType.Error
          });
        }

        if (el.Constraints.MinLength is { } minLen && text.Length < minLen)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationMinLength"],
            Code = "form.validation.minLength",
            Type = MessageType.Error
          });
        }

        if (el.Type == FormFieldType.Email && !IsValidEmail(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationEmail"],
            Code = "form.validation.email",
            Type = MessageType.Error
          });
        }
        else if (el.Model is FormPhoneModel && !IsValidPhone(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationPhone"],
            Code = "form.validation.phone",
            Type = MessageType.Error
          });
        }
        else if (el.Type == FormFieldType.Url && !IsValidSite(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationUrl"],
            Code = "form.validation.url",
            Type = MessageType.Error
          });
        }
      }

      if ((el.Constraints.Min.HasValue || el.Constraints.Max.HasValue) &&
          FilterNodeEvaluator.TryConvertToDecimal(val, out var decVal) &&
          !(isHiddenNow && decVal == 0m && !isRequiredNow))
      {
        var outOfRange = false;
        if (el.Constraints.Min is { } minDec &&
            (el.Constraints.ExclusiveMin == true ? decVal <= minDec : decVal < minDec))
        {
          outOfRange = true;
        }

        if (el.Constraints.Max is { } maxDec && decVal > maxDec)
        {
          outOfRange = true;
        }

        if (outOfRange)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationRange"],
            Code = "form.validation.range",
            Type = MessageType.Error
          });
        }
      }

      if (val is decimal decNumber && (el.Constraints.Precision.HasValue || el.Constraints.Scale.HasValue))
      {
        if (!FitsDecimal(decNumber, el.Constraints.Precision, el.Constraints.Scale))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationDecimal"],
            Code = "form.validation.decimal",
            Type = MessageType.Error
          });
        }
      }

      if (el.Model is FormSelectModel { Options: { Count: > 0 } options } &&
          TryExtractIntegralOrEnumId(val, out var optId) &&
          !options.Any(o => o.Id == optId) &&
          (optId != 0 || isRequiredNow))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = l["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormTimeZoneModel { Zones: { } zones } && val is string zone && !string.IsNullOrEmpty(zone) && !zones.Contains(zone, StringComparer.Ordinal))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = l["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormCheckboxSetModel { Options: { } checkOptions } && val is IEnumerable checkEnum and not string)
      {
        var allowedIds = checkOptions.Select(o => o.Id).ToHashSet();
        var invalidCheck = false;
        foreach (var item in checkEnum)
        {
          if (!TryExtractIntegralOrEnumId(item, out var cid) || !allowedIds.Contains(cid))
          {
            invalidCheck = true;
            break;
          }
        }

        if (invalidCheck)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
      else if (el.Model is FormCheckboxTreeModel { Nodes: { } treeNodes } && val is IEnumerable<string> treeKeys)
      {
        var keyList = treeKeys.ToList();
        var allowedKeys = RightsTree.FlattenKeys(treeNodes);
        var disabledKeys = RightsTree.FlattenDisabledKeys(treeNodes);
        var origTreeSet = ruleCtx.SaveContext.OriginalValues != null
          && ruleCtx.SaveContext.OriginalValues.TryGetValue(el.Path, out var origTreeVal)
          && origTreeVal is IEnumerable<string> origTreeEnum
            ? origTreeEnum.ToHashSet(StringComparer.Ordinal)
            : [];

        if (keyList.Any(k => !allowedKeys.Contains(k) || disabledKeys.Contains(k) && !origTreeSet.Contains(k)))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = l["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
    }
  }

  public static async Task ValidateUniqueElementsAsync<TEntity>(
    PlatformDbContext db,
    Localizer l,
    IReadOnlyList<FormElementDto> elements,
    TEntity entity,
    bool isNew,
    RuleContext<TEntity> ruleCtx,
    CancellationToken ct)
    where TEntity : class, IEntity
  {
    foreach (var el in elements)
    {
      var binding = FormElementBinding.TryGet(el);
      if (binding?.UniqueWithin == null || string.IsNullOrEmpty(el.Path) || el.Path.Contains('.'))
      {
        continue;
      }

      var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (prop == null || !prop.CanRead)
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(entity, prop.Name);
      if (val is null || val is string str && string.IsNullOrWhiteSpace(str))
      {
        continue;
      }

      var param = Expression.Parameter(typeof(TEntity), "e");
      Expression cond = Expression.Equal(Expression.Property(param, prop), Expression.Constant(val, prop.PropertyType));
      var skip = false;

      foreach (var withinName in binding.UniqueWithin)
      {
        var withinProp = PropertyAccessorCache.GetProperty(typeof(TEntity), withinName);
        if (withinProp == null || !withinProp.CanRead)
        {
          skip = true;
          break;
        }

        var withinVal = PropertyAccessorCache.GetValue(entity, withinProp.Name);
        if (withinVal is null && withinProp.PropertyType.IsValueType && Nullable.GetUnderlyingType(withinProp.PropertyType) == null)
        {
          skip = true;
          break;
        }

        var rhs = withinVal is null
          ? Expression.Constant(null, withinProp.PropertyType)
          : Expression.Constant(withinVal, withinProp.PropertyType);
        cond = Expression.AndAlso(cond, Expression.Equal(Expression.Property(param, withinProp), rhs));
      }

      if (skip)
      {
        continue;
      }

      if (!isNew && entity.Id > 0)
      {
        var idProp = PropertyAccessorCache.GetProperty(typeof(TEntity), nameof(IEntity.Id))!;
        cond = Expression.AndAlso(cond, Expression.NotEqual(Expression.Property(param, idProp), Expression.Constant(entity.Id, typeof(long))));
      }

      var exists = await db.Set<TEntity>().AsNoTracking().AnyAsync(Expression.Lambda<Func<TEntity, bool>>(cond, param), ct);
      if (exists)
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = l[binding.UniqueMessage ?? "FormValidationUnique"],
          Code = "form.validation.unique",
          Type = MessageType.Error
        });
      }
    }
  }

  public static async Task ValidateReferenceElementsAsync<TEntity>(
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer l,
    IReadOnlyList<FormElementDto> elements,
    TEntity entity,
    bool isNew,
    IReadOnlyDictionary<string, object?>? originalValues,
    RuleContext<TEntity> ruleCtx,
    CancellationToken ct,
    IReadOnlySet<string>? disabledElementPaths = null)
    where TEntity : class
  {
    foreach (var el in elements)
    {
      var isDisabledForInput = disabledElementPaths != null
        ? el.IsDisabled || disabledElementPaths.Contains(el.Path)
        : FormValueBinder.IsElementDisabledOnState(el, entity);

      if (el.ValueKind != FormValueKind.Reference && el.ValueKind != FormValueKind.List ||
          isDisabledForInput ||
          el.IsHidden ||
          el.IsServerOwned ||
          string.IsNullOrEmpty(el.Path) ||
          el.Path.Contains('.'))
      {
        continue;
      }

      if (ruleCtx.Failures.Any(f => string.Equals(f.Path, el.Path, StringComparison.OrdinalIgnoreCase)))
      {
        continue;
      }

      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp == null)
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(entity, entityProp.Name);
      var selectModel = el.Model as FormSelectModel;

      if (selectModel is { Options.Count: > 0, ReferenceFilter: null } &&
          (selectModel.ReferenceDraftRules == null || selectModel.ReferenceDraftRules.Count == 0) &&
          string.IsNullOrEmpty(selectModel.ReferenceService))
      {
        continue;
      }

      var refGridType = FormElementBinding.TryGet(el)?.ReferenceServiceType
        ?? (selectModel?.ReferenceService is { Length: > 0 } rs ? ModuleCatalog.Instance.FindServiceType(rs) : null);
      if (refGridType == null)
      {
        continue;
      }

      var isDynamicallyHidden = FormValueBinder.IsElementHidden(el, entity);
      var draftRulesChanged = false;
      if (!isNew && !isDynamicallyHidden && originalValues != null && selectModel?.ReferenceDraftRules is { Count: > 0 } draftRulesForCheck)
      {
        foreach (var dr in draftRulesForCheck)
        {
          var dp = PropertyAccessorCache.GetProperty(typeof(TEntity), dr.DraftField);
          if (dp != null)
          {
            var curDraft = PropertyAccessorCache.GetValue(entity, dp.Name);
            originalValues.TryGetValue(dp.Name, out var origDraft);
            if (!Equals(curDraft, origDraft))
            {
              draftRulesChanged = true;
              break;
            }
          }
        }
      }

      List<long> idsToValidate;
      if (el.ValueKind == FormValueKind.List)
      {
        if (val is not IEnumerable listEnum || val is string)
        {
          continue;
        }

        var rawIds = new List<long>();
        var hasNonPositive = false;
        foreach (var item in listEnum)
        {
          if (TryExtractIntegralOrEnumId(item, out var parsedId))
          {
            if (parsedId <= 0)
            {
              hasNonPositive = true;
            }
            else
            {
              rawIds.Add(parsedId);
            }
          }
          else if (item != null && long.TryParse(item.ToString(), out var strParsed))
          {
            if (strParsed <= 0)
            {
              hasNonPositive = true;
            }
            else
            {
              rawIds.Add(strParsed);
            }
          }
        }

        if (hasNonPositive)
        {
          AddReferenceFailure(ruleCtx, l, el.Path);
          continue;
        }

        idsToValidate = rawIds.Distinct().ToList();
        if (!isNew &&
            !draftRulesChanged &&
            originalValues != null &&
            originalValues.TryGetValue(entityProp.Name, out var origListVal) &&
            origListVal is IEnumerable origEnum and not string)
        {
          var origSet = new HashSet<long>();
          foreach (var oItem in origEnum)
          {
            if (TryExtractIntegralOrEnumId(oItem, out var ol))
            {
              origSet.Add(ol);
            }
            else if (oItem != null && long.TryParse(oItem.ToString(), out var op))
            {
              origSet.Add(op);
            }
          }

          idsToValidate = idsToValidate.Where(id => !origSet.Contains(id)).ToList();
        }

        if (idsToValidate.Count == 0)
        {
          continue;
        }
      }
      else
      {
        if (val is null || !TryExtractIntegralOrEnumId(val, out var refId))
        {
          continue;
        }

        if (refId < 0)
        {
          AddReferenceFailure(ruleCtx, l, el.Path);
          continue;
        }

        if (refId == 0)
        {
          continue;
        }

        if (!isNew &&
            !draftRulesChanged &&
            originalValues != null &&
            originalValues.TryGetValue(entityProp.Name, out var origVal) &&
            TryExtractIntegralOrEnumId(origVal, out var origId) &&
            origId == refId)
        {
          continue;
        }

        idsToValidate = [refId];
      }

      if (ModuleCatalog.Instance.CreateServiceInstance(refGridType, db, app) is not Grid.IGridService refGrid)
      {
        continue;
      }

      var filterItems = new List<FilterNode>
      {
        idsToValidate.Count == 1
          ? FilterNode.Rule("id", FilterOperator.Equals, idsToValidate[0])
          : FilterNode.Rule("id", FilterOperator.In, idsToValidate)
      };

      if (selectModel != null)
      {
        if (selectModel.ReferenceFilter != null)
        {
          filterItems.Add(selectModel.ReferenceFilter);
        }

        if (selectModel.ReferenceDraftRules is { Count: > 0 })
        {
          foreach (var draftRule in selectModel.ReferenceDraftRules)
          {
            var draftProp = PropertyAccessorCache.GetProperty(typeof(TEntity), draftRule.DraftField);
            if (draftProp != null)
            {
              var draftVal = PropertyAccessorCache.GetValue(entity, draftProp.Name);
              filterItems.Add(draftVal != null
                ? FilterNode.Rule(draftRule.SourceField, FilterOperator.Equals, draftVal)
                : FilterNode.Rule(draftRule.SourceField, FilterOperator.IsNull, null));
            }
          }
        }
      }

      var combinedFilter = filterItems.Count == 1
        ? filterItems[0]
        : FilterNode.All(filterItems.ToArray());

      var countDto = await refGrid.GetCountAsync(new GridRequest { Filter = combinedFilter }, ct);
      if (countDto.Total != idsToValidate.Count)
      {
        AddReferenceFailure(ruleCtx, l, el.Path);
      }
    }
  }

  public static bool TryExtractIntegralOrEnumId(object? value, out long id)
  {
    switch (value)
    {
      case long l:
        id = l;
        return true;
      case int i:
        id = i;
        return true;
      case short s:
        id = s;
        return true;
      case byte b:
        id = b;
        return true;
      case sbyte sb:
        id = sb;
        return true;
      case ushort us:
        id = us;
        return true;
      case uint ui:
        id = ui;
        return true;
      case ulong ul when ul <= long.MaxValue:
        id = (long)ul;
        return true;
      case Enum e:
        id = Convert.ToInt64(e, CultureInfo.InvariantCulture);
        return true;
      case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt64(out var jeId):
        id = jeId;
        return true;
      default:
        id = 0;
        return false;
    }
  }

  private static bool AnswersRequirement(object? value, bool foreignKey, FormElementModel model)
  {
    if (value is null)
    {
      return false;
    }

    if (value is string text)
    {
      return !string.IsNullOrWhiteSpace(text);
    }

    if (value is ICollection collection)
    {
      return collection.Count > 0;
    }

    if (value is Guid guid)
    {
      return guid != Guid.Empty;
    }

    if (TryExtractIntegralOrEnumId(value, out var key))
    {
      if (foreignKey)
      {
        return key > 0;
      }

      if (model is FormSelectModel { Options: { Count: > 0 } options })
      {
        return options.Any(o => o.Id == key);
      }
    }

    return true;
  }

  private static void AddRequiredFailure<TEntity>(RuleContext<TEntity> ruleCtx, Localizer l, string path)
  {
    if (!ruleCtx.Failures.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase) && f.Code == "form.validation.required"))
    {
      ruleCtx.Failures.Add(new MessageDto
      {
        Path = path,
        Detail = l["Required"],
        Code = "form.validation.required",
        Type = MessageType.Error
      });
    }
  }

  private static void AddReferenceFailure<TEntity>(RuleContext<TEntity> ruleCtx, Localizer l, string path)
  {
    ruleCtx.Failures.Add(new MessageDto
    {
      Path = path,
      Detail = l["FormValidationReference"],
      Code = "form.validation.reference",
      Type = MessageType.Error
    });
  }

  private static bool FitsDecimal(decimal value, int? precision, int? scale)
  {
    var bits = decimal.GetBits(value);
    var fractional = (bits[3] >> 16) & 0xff;
    var magnitude = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
    while (fractional > 0 && magnitude % 10 == 0)
    {
      magnitude /= 10;
      fractional--;
    }

    var digits = magnitude.IsZero ? 0 : magnitude.ToString(CultureInfo.InvariantCulture).Length;
    var integerDigits = Math.Max(0, digits - fractional);
    return (scale is null || fractional <= scale) &&
           (precision is null || (scale is { } s ? integerDigits <= precision - s : Math.Max(digits, fractional) <= precision));
  }

  private static bool IsValidEmail(string value)
  {
    var at = value.IndexOf('@');
    return at > 0 && at < value.Length - 1 && at == value.LastIndexOf('@');
  }

  private static bool IsValidPhone(string value) =>
    value is ['+', .. var digits] && digits.Length is >= 7 and <= 15 && digits.All(char.IsAsciiDigit);

  private static bool IsValidSite(string value) =>
    IsWebAddress(value, prefixed: false) || IsWebAddress("https://" + value, prefixed: true);

  private static bool IsWebAddress(string value, bool prefixed) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
    uri.Host.Contains('.', StringComparison.Ordinal) && !uri.Host.StartsWith('.') && !uri.Host.EndsWith('.') &&
    !(prefixed && uri.UserInfo.Length > 0);
}
