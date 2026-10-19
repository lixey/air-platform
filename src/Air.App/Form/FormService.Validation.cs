using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Text.Json;
using Air.App.Components;
using Air.App.Internal;
using Air.App.Screens;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  private async Task<(FormSaveContext<TEntity, TInput> SaveContext, RuleContext<TInput> RuleContext)> RunValidationPipelineAsync(
    Elements elements,
    Hooks hooks,
    TEntity entity,
    TInput input,
    IReadOnlyDictionary<string, object?>? originalValues,
    CancellationToken ct)
  {
    var saveCtx = new FormSaveContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew,
      OriginalValues = originalValues
    };

    foreach (var h in hooks.BeforeValidateActions)
    {
      h(saveCtx);
    }

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TInput>
    {
      Target = input,
      SaveContext = new FormSaveContext<object, TInput>
      {
        Entity = entity,
        Input = input,
        IsNew = IsNew,
        OriginalValues = originalValues
      }
    };

    ValidateElementConstraints(elements, entity, input, ruleCtx, onlyAcceptingInput: true);

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    await ValidateUniqueElementsAsync(elements, entity, ruleCtx, ct);
    return (saveCtx, ruleCtx);
  }

  private void ValidateElementConstraints(
    Elements elements,
    TEntity entity,
    TInput input,
    RuleContext<TInput> ruleCtx,
    bool onlyAcceptingInput)
  {
    var efEntityType = db.Model.FindEntityType(typeof(TEntity));

    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path)) continue;

      var acceptsInput = !el.IsDisabled && !el.IsServerOwned && el.Model is not FormInfoModel;
      if (onlyAcceptingInput)
      {
        if (!acceptsInput) continue;
      }
      else
      {
        if (acceptsInput || el.IsInputOnly || !el.IsRequired) continue;
      }

      var inputProp = PropertyAccessorCache.GetProperty(typeof(TInput), el.Path);
      var entityProp = !el.IsInputOnly ? PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path) : null;
      if (inputProp == null && entityProp == null) continue;

      var val = inputProp != null
        ? PropertyAccessorCache.GetValue(input, inputProp.Name)
        : PropertyAccessorCache.GetValue(entity, entityProp!.Name);

      var efProp = efEntityType != null && entityProp != null ? efEntityType.FindProperty(entityProp.Name) : null;
      var isFk = el.ValueKind == FormValueKind.Reference || !string.IsNullOrEmpty(el.ReferencePath) || (efProp?.IsForeignKey() ?? false);

      if (el.IsRequired && !AnswersRequirement(val, isFk))
      {
        if (!ruleCtx.Failures.Any(f => string.Equals(f.Path, el.Path, StringComparison.OrdinalIgnoreCase) && f.Code == "form.validation.required"))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["Required"],
            Code = "form.validation.required",
            Type = MessageType.Error
          });
        }
        continue;
      }

      if (!onlyAcceptingInput) continue;

      if (val is null)
      {
        if (el.Constraints.AllowsNull == false && !el.IsRequired)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationNull"],
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
            Detail = L["FormValidationMaxLength"],
            Code = "form.validation.maxLength",
            Type = MessageType.Error
          });
        }
        if (el.Constraints.MinLength is { } minLen && text.Length < minLen)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationMinLength"],
            Code = "form.validation.minLength",
            Type = MessageType.Error
          });
        }

        if (el.Type == FormFieldType.Email && !IsValidEmail(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationEmail"],
            Code = "form.validation.email",
            Type = MessageType.Error
          });
        }
        else if (el.Model is FormPhoneModel && !IsValidPhone(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationPhone"],
            Code = "form.validation.phone",
            Type = MessageType.Error
          });
        }
        else if (el.Type == FormFieldType.Url && !IsValidSite(text))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationUrl"],
            Code = "form.validation.url",
            Type = MessageType.Error
          });
        }
      }

      if ((el.Constraints.Min.HasValue || el.Constraints.Max.HasValue) && TryConvertToDecimal(val, out var decVal))
      {
        var outOfRange = false;
        if (el.Constraints.Min is { ValueKind: JsonValueKind.Number } minEl && minEl.TryGetDecimal(out var minDec) && decVal < minDec)
        {
          outOfRange = true;
        }
        if (el.Constraints.Max is { ValueKind: JsonValueKind.Number } maxEl && maxEl.TryGetDecimal(out var maxDec) && decVal > maxDec)
        {
          outOfRange = true;
        }
        if (outOfRange)
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationRange"],
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
            Detail = L["FormValidationDecimal"],
            Code = "form.validation.decimal",
            Type = MessageType.Error
          });
        }
      }

      if (el.Model is FormSelectModel { Options: { } options } && val is long optId && optId > 0 && !options.Any(o => o.Id == optId))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = L["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormTimeZoneModel { Zones: { } zones } && val is string zone && !string.IsNullOrEmpty(zone) && !zones.Contains(zone, StringComparer.Ordinal))
      {
        ruleCtx.Failures.Add(new MessageDto
        {
          Path = el.Path,
          Detail = L["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }
      else if (el.Model is FormCheckboxSetModel { Options: { } checkOptions } && val is IEnumerable<long> checkIds)
      {
        var allowedIds = checkOptions.Select(o => o.Id).ToHashSet();
        if (checkIds.Any(id => !allowedIds.Contains(id)))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
      else if (el.Model is FormCheckboxTreeModel { Nodes: { } treeNodes } && val is IEnumerable<string> treeKeys)
      {
        var allowedKeys = Air.App.Auth.RightsTree.FlattenKeys(treeNodes);
        if (treeKeys.Any(k => !allowedKeys.Contains(k)))
        {
          ruleCtx.Failures.Add(new MessageDto
          {
            Path = el.Path,
            Detail = L["FormValidationOption"],
            Code = "form.validation.option",
            Type = MessageType.Error
          });
        }
      }
    }
  }

  private static bool AnswersRequirement(object? value, bool foreignKey) => value switch
  {
    null => false,
    string text => !string.IsNullOrWhiteSpace(text),
    ICollection collection => collection.Count > 0,
    Guid guid => guid != Guid.Empty,
    int key when foreignKey => key > 0,
    long key when foreignKey => key > 0,
    _ => true
  };

  private static bool TryConvertToDecimal(object value, out decimal result)
  {
    switch (value)
    {
      case decimal d: result = d; return true;
      case int i: result = i; return true;
      case long l: result = l; return true;
      case short s: result = s; return true;
      case byte b: result = b; return true;
      case double dbVal and not double.NaN and not double.PositiveInfinity and not double.NegativeInfinity:
        result = (decimal)dbVal; return true;
      case float flVal and not float.NaN and not float.PositiveInfinity and not float.NegativeInfinity:
        result = (decimal)flVal; return true;
      default:
        result = 0; return false;
    }
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

  private async Task ValidateUniqueElementsAsync(Elements elements, TEntity entity, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    foreach (var el in elements.List)
    {
      if (el.UniqueWithin == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (prop == null || !prop.CanRead)
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(entity, prop.Name);
      if (val is null || (val is string str && string.IsNullOrWhiteSpace(str)))
      {
        continue;
      }

      var param = Expression.Parameter(typeof(TEntity), "e");
      Expression cond = Expression.Equal(Expression.Property(param, prop), Expression.Constant(val, prop.PropertyType));
      var skip = false;

      foreach (var withinName in el.UniqueWithin)
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

        cond = Expression.AndAlso(
          cond,
          Expression.Equal(Expression.Property(param, withinProp), Expression.Constant(withinVal, withinProp.PropertyType)));
      }

      if (skip)
      {
        continue;
      }

      if (!IsNew && entity.Id > 0)
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
          Detail = L[el.UniqueMessage ?? "FormValidationUnique"],
          Code = "form.validation.unique",
          Type = MessageType.Error
        });
      }
    }
  }
}
