namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentProposalFieldDto
{
  public string Label { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }
}
