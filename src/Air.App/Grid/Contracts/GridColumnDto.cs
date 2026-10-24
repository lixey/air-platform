namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridColumnDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? FilterPath { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [OneOf(typeof(UiFieldType))]
  public string Type { get; set; } = UiFieldType.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiTemporalType))]
  public string? TemporalType { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Width { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxWidth { get; set; }

  public bool IsSortable { get; set; } = true;
  public bool IsFilterable { get; set; } = true;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<GridFilterOperatorDto>? Operators { get; set; }

  public bool IsResizable { get; set; } = true;
  public bool IsHidden { get; set; }
  public bool IsDisabled { get; set; }
  public bool IsRequired { get; set; }
  public bool IsSticky { get; set; }
  public bool IsStickyRight { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsWrapped { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsEditable { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool ConfirmsEdit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ConfirmText { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferenceService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? ReferenceLabelPaths { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? ReferenceFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public GridNumberEditDto? NumberEdit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<OptionDto>? Options { get; set; }

  public CellModel Cell { get; set; } = new GridCellTextModel();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public HeaderModel? Header { get; set; }

  [JsonIgnore]
  internal Func<object, string?>? ColorEvaluator { get; set; }

  [JsonIgnore]
  internal System.Linq.Expressions.LambdaExpression? ColorExpression { get; set; }

  [JsonIgnore]
  internal string? ColorNavPath { get; set; }

  [JsonIgnore]
  internal bool IsSummed { get; set; }

  [JsonIgnore]
  internal bool IsComputed { get; set; }

  [JsonIgnore]
  internal Type? MemberType { get; set; }

  [JsonIgnore]
  internal Func<object, Type?>? FormServiceResolver { get; set; }

  [JsonIgnore]
  internal System.Linq.Expressions.LambdaExpression? FilterExpression { get; set; }
}
