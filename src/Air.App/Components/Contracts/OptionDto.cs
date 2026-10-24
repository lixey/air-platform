namespace Platform.App.Components.Contracts;

[UiContract]
public sealed class OptionDto
{
  public long Id { get; init; }
  public string Name { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }
}
