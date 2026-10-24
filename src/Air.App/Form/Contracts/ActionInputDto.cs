namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class ActionInputDto
{
  public Dictionary<string, object?> Value { get; set; } = new(StringComparer.Ordinal);
}
