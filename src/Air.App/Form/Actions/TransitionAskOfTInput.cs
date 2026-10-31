using Platform.App.Internals;

namespace Platform.App.Form.Actions;

public sealed class TransitionAsk<TInput>(Action<ActionInput<TInput>> elements) : RecordAsk where TInput : class, new()
{
  public override IReadOnlyList<FormElementDto> BuildElements()
  {
    var input = new ActionInput<TInput>();
    elements(input);
    return input.Elements;
  }

  public override object? Read(JsonElement? input)
  {
    if (input is null || input.Value.ValueKind != JsonValueKind.Object)
    {
      return new TInput();
    }

    return input.Value.Deserialize<TInput>(PlatformJsonOptions.DefaultOptions) ?? new TInput();
  }

  public override IReadOnlyList<MessageDto> Validate(object? input, ILocalizer localizer)
  {
    var built = BuildElements();
    var errors = new List<MessageDto>();
    var target = input as TInput ?? new TInput();

    foreach (var el in built)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var val = PropertyAccessorCache.GetValue(target, el.Path);
      var isSelectOrFk = el.ValueKind == FormValueKind.Reference || !string.IsNullOrEmpty(el.ReferencePath) || el.Model is FormSelectModel;

      if (el.IsRequired && (val is null || val is string s && string.IsNullOrWhiteSpace(s) || isSelectOrFk && (val is long l && l <= 0 || val is int i && i <= 0)))
      {
        errors.Add(new MessageDto
        {
          Path = el.Path,
          Detail = localizer["Required"],
          Code = "form.validation.required",
          Type = MessageType.Error
        });
        continue;
      }

      if (el.Model is FormSelectModel { Options: { Count: > 0 } options } && val is long optId && optId > 0 && !options.Any(o => o.Id == optId))
      {
        errors.Add(new MessageDto
        {
          Path = el.Path,
          Detail = localizer["FormValidationOption"],
          Code = "form.validation.option",
          Type = MessageType.Error
        });
      }

      if (val is string str && str.Length > 0)
      {
        if (el.Constraints.MaxLength is { } maxLen && str.Length > maxLen)
        {
          errors.Add(new MessageDto
          {
            Path = el.Path,
            Detail = localizer["FormValidationMaxLength"],
            Code = "form.validation.maxLength",
            Type = MessageType.Error
          });
        }

        if (el.Constraints.MinLength is { } minLen && str.Length < minLen)
        {
          errors.Add(new MessageDto
          {
            Path = el.Path,
            Detail = localizer["FormValidationMinLength"],
            Code = "form.validation.minLength",
            Type = MessageType.Error
          });
        }
      }

      if (val != null && TryConvertToDecimal(val, out var decVal))
      {
        var outOfRange =
          el.Constraints.Min is { ValueKind: JsonValueKind.Number } minJe && minJe.TryGetDecimal(out var minDec) && decVal < minDec ||
          el.Constraints.Max is { ValueKind: JsonValueKind.Number } maxJe && maxJe.TryGetDecimal(out var maxDec) && decVal > maxDec;
        if (outOfRange)
        {
          errors.Add(new MessageDto
          {
            Path = el.Path,
            Detail = localizer["FormValidationRange"],
            Code = "form.validation.range",
            Type = MessageType.Error
          });
        }
      }
    }

    return errors;
  }

  private static bool TryConvertToDecimal(object value, out decimal result)
  {
    switch (value)
    {
      case decimal d: result = d; return true;
      case int i: result = i; return true;
      case long l: result = l; return true;
      case short s: result = s; return true;
      case byte b: result = b; return true;
      case double db when !double.IsNaN(db) && !double.IsInfinity(db): result = (decimal)db; return true;
      case float f when !float.IsNaN(f) && !float.IsInfinity(f): result = (decimal)f; return true;
      default:
        result = 0;
        return false;
    }
  }
}
