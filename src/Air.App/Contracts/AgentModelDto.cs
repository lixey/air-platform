namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentModelDto
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Provider { get; set; }

  public bool IsDefault { get; set; }

  public bool IsForPhotos { get; set; }
}
