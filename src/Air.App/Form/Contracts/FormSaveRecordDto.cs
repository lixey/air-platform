namespace Platform.App.Form.Contracts;

public sealed class FormSaveRecordDto
{
  public string Outcome { get; init; } = MutationOutcome.Committed;
  public long Id { get; init; }
  public int? Ver { get; init; }
  public List<MessageDto> Messages { get; init; } = [];
  public List<MessageDto> Warnings { get; init; } = [];
  public List<AffectedDto>? Affected { get; init; }

  [JsonIgnore]
  public object? Entity { get; init; }
}
