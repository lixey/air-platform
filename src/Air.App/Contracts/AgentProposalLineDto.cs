namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentProposalLineDto
{
  public string Name { get; set; } = string.Empty;
  public decimal Qty { get; set; }
  public decimal Price { get; set; }
  public decimal Amount { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Category { get; set; }
}
