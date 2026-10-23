namespace Air.App.Contracts;

[UiContract]
public sealed class AgentAttachmentDto
{
  public string Id { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public long Size { get; set; }
  public string PreviewUrl { get; set; } = string.Empty;

  [JsonIgnore]
  public string LocalPath { get; set; } = string.Empty;
}

[UiContract]
public sealed class AgentProposalFieldDto
{
  public string Label { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Badge { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }
}

[UiContract]
public sealed class AgentProposalLineDto
{
  public string Name { get; set; } = string.Empty;
  public decimal Qty { get; set; }
  public decimal Price { get; set; }
  public decimal Amount { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Category { get; set; }
}

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
  public Overlay.OverlayModelDto? Overlay { get; set; }
}

[UiContract]
public sealed class AgentCommitResultDto
{
  public string ProposalId { get; set; } = string.Empty;
  public long RecordId { get; set; }
  public string RecordLabel { get; set; } = string.Empty;
  public string FormService { get; set; } = string.Empty;
  public List<string> RefreshServices { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<AgentCommitResultDto>? AlsoCommitted { get; set; }
}

[UiContract]
public sealed class AgentStreamEventDto
{
  public string Type { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Text { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public AgentProposalDto? Proposal { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }
}

[UiContract]
[ClientOptional]
public sealed class AgentChatRequestDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Message { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? AttachmentIds { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Locale { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Provider { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Model { get; set; }
}

[UiContract]
[ClientOptional]
public sealed class AgentResetRequestDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }
}

[UiContract]
public sealed class AgentModelDto
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Provider { get; set; }

  public bool IsDefault { get; set; }
}

[UiContract]
public sealed class AgentProviderDto
{
  public string Handle { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsAvailable { get; set; }
}

[UiContract]
public sealed class AgentStatusDto
{
  public string ActiveProvider { get; set; } = string.Empty;
  public string DefaultModel { get; set; } = string.Empty;
  public List<AgentProviderDto> Providers { get; set; } = [];
  public List<AgentModelDto> Models { get; set; } = [];
}
