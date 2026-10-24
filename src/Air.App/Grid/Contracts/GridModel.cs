namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public List<GridColumnDto> Columns { get; set; } = [];
  public List<List<GridHeaderCellDto>> Headers { get; set; } = [];
  public NavModel Nav { get; set; } = new();
  public NavModel ActionNav { get; set; } = new();
  public int PageSize { get; set; } = 200;
  public bool IsSelectable { get; set; } = true;
  public bool IsReadOnly { get; set; }
  public MutationTextsDto MutationTexts { get; set; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayDto? Form { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? InitialSearch { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? InitialFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool FocusesSearch { get; set; }
}
