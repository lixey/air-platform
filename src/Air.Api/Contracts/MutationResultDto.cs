namespace Platform.Api.Contracts;

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
