namespace Air.App.Screens;

public abstract class RecordAsk
{
  public abstract IReadOnlyList<FormElementDto> BuildElements();
  public abstract object? Read(JsonElement? input);
  public virtual IReadOnlyList<MessageDto> Validate(object? input, ILocalizer localizer) => [];
}
