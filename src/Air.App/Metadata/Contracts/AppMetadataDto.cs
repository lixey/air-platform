namespace Platform.App.Metadata.Contracts;

[UiContract]
public class AppMetadataDto
{
  public List<MenuItemDto> Menu { get; set; } = [];
  public List<MenuItemDto> Routes { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public SwitcherDto? Tenant { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public SwitcherDto? Language { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? DefaultCurrency { get; set; }

  public Dictionary<string, string> Texts { get; set; } = [];
}
