using System.Text.Json;
using Air.App.Form;

namespace Air.App.Screens;

public abstract class RecordAsk
{
  public abstract IReadOnlyList<FormElementDto> BuildElements();
  public abstract object? Read(JsonElement? input);
}
