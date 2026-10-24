namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentProposalDto
{
  public string Id { get; set; } = string.Empty;
  public string SessionId { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public string Service { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? BadgeColor { get; set; }

  public List<AgentProposalFieldDto> Fields { get; set; } = [];
  public List<AgentProposalLineDto> Lines { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? LinesLabel { get; set; }

  public List<string> AttachmentIds { get; set; } = [];

  [JsonIgnore]
  public JsonElement Payload { get; set; }

  public string Status { get; set; } = "pending";

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? RecordId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? CommittedRecordId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? CommittedLabel { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? FormService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayDto? Overlay { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? DuplicateWarning { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? DuplicateRecordId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? DuplicateFormService { get; set; }
}
