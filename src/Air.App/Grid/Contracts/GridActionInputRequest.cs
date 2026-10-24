namespace Platform.App.Grid.Contracts;

[UiContract]
[ClientOptional]
public class GridActionInputRequest : GridRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Method { get; set; }
}
