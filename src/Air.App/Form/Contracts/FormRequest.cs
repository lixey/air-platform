namespace Platform.App.Form.Contracts;

[UiContract]
[ClientOptional]
public class FormRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? Id { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ParentService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? ParentId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Ver { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public JsonElement? Value { get; set; }

  [JsonIgnore]
  public string? IdempotencyKey { get; set; }

  [JsonIgnore]
  public string? RawPayload { get; set; }
}
