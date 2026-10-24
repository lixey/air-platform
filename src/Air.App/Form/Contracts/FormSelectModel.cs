namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormSelectModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferenceService { get; set; }

  public List<string> ReferenceLabelPaths { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<OptionDto>? Options { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? ReferenceFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReferenceDraftRuleDto>? ReferenceDraftRules { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsMultiple { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayDto? CreateOverlay { get; set; }
}
