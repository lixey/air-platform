namespace Platform.App.Metadata.Contracts;

[UiContract]
public class MenuItemDto
{
  public string? Path { get; set; }
  public string? Handle { get; set; }
  public string? Name { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiIcon))]
  public string? Icon { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Screen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsSeparator { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  public List<MenuItemDto> Items { get; set; } = [];
}
