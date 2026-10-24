namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class FormDataDto : MutationResultDto
{
  public long? Id { get; set; }
  public int? Ver { get; set; }
  public Dictionary<string, object?> Value { get; set; } = new(StringComparer.Ordinal);

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, int>? Unavailable { get; set; }
}
