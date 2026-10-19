using System.Text.Json;
using Air.App.Internal;
using Air.App.Localization;
using Air.App.Screens;

namespace Air.App.Form.Actions;

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
      if (string.IsNullOrEmpty(el.Path)) continue;
      var val = PropertyAccessorCache.GetValue(target, el.Path);

      if (el.IsRequired && (val is null || (val is string s && string.IsNullOrWhiteSpace(s))))
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
    }

    return errors;
  }
}
