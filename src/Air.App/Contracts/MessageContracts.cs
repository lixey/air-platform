namespace Air.App.Contracts;

[UiContract]
public static class MessageType
{
  public const string Success = "success";
  public const string Error = "error";
  public const string Warning = "warning";
  public const string Info = "info";
}

[UiContract]
public static class MutationOutcome
{
  public const string Committed = "committed";
  public const string Refused = "refused";
  public const string RolledBack = "rolledBack";
  public const string Conflict = "conflict";
  public const string Unknown = "unknown";
}

[UiContract]
public class MessageDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Code { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Detail { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? RecordId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? RecordLabel { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [OneOf(typeof(MessageType))]
  public string Type { get; set; } = MessageType.Info;
}

[UiContract]
public class MutationResultDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Messages { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }
}
