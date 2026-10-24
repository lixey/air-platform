namespace Platform.App.Form.Contracts;

[UiContract]
[ClientOptional]
public sealed class FormActionInputRequest : FormRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Method { get; set; }
}
