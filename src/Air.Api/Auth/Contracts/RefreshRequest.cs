namespace Platform.Api.Auth.Contracts;

[UiContract]
public class RefreshRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? RefreshToken { get; set; }
}
