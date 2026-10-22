namespace Air.App.Report;

[UiContract]
public static class ReportChartKind
{
  public const string Bar = "bar";
  public const string Line = "line";
  public const string Area = "area";
  public const string StackedBar = "stacked-bar";
  public const string HorizontalBar = "horizontal-bar";
  public const string Donut = "donut";
  public const string Pie = "pie";
}

[UiContract]
public static class ReportColumnKind
{
  public const string Text = "text";
  public const string Money = "money";
  public const string Number = "number";
  public const string Percent = "percent";
}

[UiContract]
public sealed record ReportModel : PanelModel
{
  public string Service { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }
}

[UiContract]
public sealed class ReportRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsDataOnly { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, string>? Filters { get; set; }
}

[UiContract]
public sealed class ReportFilterItemDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}

[UiContract]
public sealed class ReportFilterGroupDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;
  public List<ReportFilterItemDto> Items { get; init; } = [];
}

[UiContract]
public sealed class ReportKpiDto
{
  public string Label { get; init; } = string.Empty;
  public decimal Value { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsPercent { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsCount { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}

[UiContract]
public sealed class ReportSeriesDto
{
  public string Name { get; init; } = string.Empty;

  [OneOf(typeof(ReportChartKind))]
  public string Kind { get; init; } = ReportChartKind.Bar;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Stack { get; init; }

  public List<decimal> Data { get; init; } = [];
}

[UiContract]
public sealed class ReportPieSliceDto
{
  public string Name { get; init; } = string.Empty;
  public decimal Value { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}

[UiContract]
public sealed class ReportChartDto
{
  public string Key { get; init; } = string.Empty;
  public string Title { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; init; }

  [OneOf(typeof(ReportChartKind))]
  public string Kind { get; init; } = ReportChartKind.Bar;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Height { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Span { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? Categories { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportSeriesDto>? Series { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportPieSliceDto>? Slices { get; init; }
}

[UiContract]
public sealed class ReportColumnDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;

  [OneOf(typeof(ReportColumnKind))]
  public string Kind { get; init; } = ReportColumnKind.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Sticky { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Width { get; init; }
}

[UiContract]
public sealed class ReportTableDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }

  public List<ReportColumnDto> Columns { get; init; } = [];
  public List<Dictionary<string, object?>> Rows { get; init; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Dictionary<string, object?>>? BottomRows { get; init; }
}

[UiContract]
public sealed class ReportDataDto
{
  public List<ReportFilterGroupDto> FilterGroups { get; init; } = [];
  public Dictionary<string, string> ActiveFilters { get; init; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportKpiDto>? Kpis { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportChartDto>? Charts { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ReportTableDto? Table { get; init; }
}
