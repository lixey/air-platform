using Air.Data.Contracts;

namespace Air.App.Metadata;

[UiContract]
public class MenuItemDto
{
  public string? Path { get; set; }
  public string? Handle { get; set; }
  public string? Name { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Icon { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Screen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsSeparator { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  public List<MenuItemDto> Items { get; set; } = [];
}

[UiContract]
public record SwitcherDto
{
  public string Label { get; set; } = string.Empty;
  public Reference Current { get; set; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Reference>? Options { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Service { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Method { get; set; }
}

[UiContract]
public sealed record SwitchRequest
{
  public long Id { get; set; }
}

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

[UiContract]
public sealed class MetadataDto : AppMetadataDto;
