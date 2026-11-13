namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentProposalDto
{
  public string Id { get; set; } = string.Empty;
  public string SessionId { get; set; } = string.Empty;

  [OneOf(typeof(AgentProposalOperation))]
  public string Operation { get; set; } = AgentProposalOperation.Create;

  public string Service { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Transition { get; set; }

  public string Title { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? BadgeColor { get; set; }

  public List<AgentProposalFieldDto> Fields { get; set; } = [];
  public List<AgentProposalChildGroupDto> Children { get; set; } = [];

  public List<string> AttachmentIds { get; set; } = [];

  [JsonIgnore]
  public JsonElement Payload { get; set; }

  [OneOf(typeof(AgentProposalStatus))]
  public string Status { get; set; } = AgentProposalStatus.Pending;

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

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? StepKey { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? StepNumber { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? DependsOn { get; set; }

  [JsonIgnore]
  public string? TurnId { get; set; }
}
