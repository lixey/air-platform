namespace Platform.App.Files.Contracts;

[UiContract]
public sealed class FileMoveRequest : GridRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? BeforeId { get; set; }
}
