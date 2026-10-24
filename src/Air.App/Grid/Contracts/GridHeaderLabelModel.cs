namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridHeaderLabelModel : HeaderModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiIcon))]
  public string? Icon { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? Filter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsHighlighted { get; set; }
}
