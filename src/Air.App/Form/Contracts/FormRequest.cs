namespace Platform.App.Form.Contracts;

[UiContract]
[ClientOptional]
public class FormRequest : IScopedServiceRequest
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

  private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

  internal static (FormRequest Request, JsonElement EntityJson) Normalize(
    JsonElement json,
    FormRequest? existing = null)
  {
    var req = existing ?? new FormRequest();
    var hasPresetValue = req.Value is { ValueKind: JsonValueKind.Object };
    var entityJson = hasPresetValue
      ? req.Value!.Value
      : EmptyObject;

    if (json.ValueKind == JsonValueKind.Object)
    {
      req.RawPayload ??= json.GetRawText();

      if (!hasPresetValue && json.TryGetProperty("value", out var valProp))
      {
        if (valProp.ValueKind != JsonValueKind.Object)
        {
          throw new JsonException("FormRequest 'value' must be a JSON object.");
        }

        entityJson = valProp.Clone();
        req.Value = entityJson;
      }

      if (!hasPresetValue)
      {
        if (req.Id is null or <= 0 && TryReadLong(json, "id") is { } rootId)
        {
          req.Id = rootId;
        }

        if (req.Ver is null && TryReadInt(json, "ver") is { } rootVer)
        {
          req.Ver = rootVer;
        }

        if (req.ParentId is null && TryReadLong(json, "parentId") is { } rootPid)
        {
          req.ParentId = rootPid;
        }

        if (string.IsNullOrEmpty(req.ParentService) &&
            json.TryGetProperty("parentService", out var psProp) &&
            psProp.ValueKind == JsonValueKind.String &&
            psProp.GetString() is { } rootPs)
        {
          req.ParentService = rootPs;
        }
      }
    }

    return (req, entityJson);
  }

  private static long? TryReadLong(JsonElement obj, string propName)
  {
    if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propName, out var prop))
    {
      return null;
    }

    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var num) && num > 0)
    {
      return num;
    }

    if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var strNum) && strNum > 0)
    {
      return strNum;
    }

    return null;
  }

  private static int? TryReadInt(JsonElement obj, string propName) =>
    obj.ValueKind == JsonValueKind.Object &&
    obj.TryGetProperty(propName, out var prop) &&
    prop.ValueKind == JsonValueKind.Number &&
    prop.TryGetInt32(out var num)
      ? num
      : null;
}
