namespace Platform.App.Grid.Contracts;

[UiContract]
[ClientOptional]
public class GridCellRequest : GridRequest
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Ver { get; set; }

  public JsonElement Value { get; set; }
}
