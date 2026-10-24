namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class FormActionDto : MutationResultDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OpenScreen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? OpenId { get; set; }
}
