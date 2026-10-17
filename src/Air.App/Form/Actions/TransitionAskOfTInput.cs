using System.Text.Json;
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
}
