namespace Platform.App.Grid.Contracts;

public class GridPageDto<TRow> : Page<TRow>
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<TRow>? TopRows { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<TRow>? BottomRows { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, OverlayDto>? Forms { get; init; }
}
