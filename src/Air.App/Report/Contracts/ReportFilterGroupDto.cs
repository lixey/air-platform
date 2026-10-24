namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportFilterGroupDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;
  public List<ReportFilterItemDto> Items { get; init; } = [];
}
