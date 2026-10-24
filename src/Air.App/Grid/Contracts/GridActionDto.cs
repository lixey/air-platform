namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridActionDto : MutationResultDto
{
  public int Count { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? Id { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OpenScreen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? OpenId { get; set; }
}
