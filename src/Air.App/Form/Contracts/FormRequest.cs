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

  internal static (FormRequest Request, JsonElement EntityJson) Normalize(
    JsonElement json,
    FormRequest? existing = null,
    bool allowNestedFallback = true)
  {
    var req = existing ?? new FormRequest();
    var entityJson = json;
    if (json.ValueKind == JsonValueKind.Object)
    {
      req.RawPayload ??= json.GetRawText();

      if (json.TryGetProperty("value", out var valProp) || json.TryGetProperty("entity", out valProp))
      {
        entityJson = valProp;
        if (valProp.ValueKind == JsonValueKind.Object)
        {
          req.Value ??= valProp.Clone();
        }
      }

      if (req.Id is null or <= 0)
      {
        if (TryReadLong(json, "id") is { } rootId)
        {
          req.Id = rootId;
        }
        else if (allowNestedFallback && TryReadLong(entityJson, "id") is { } nestedId)
        {
          req.Id = nestedId;
        }
      }

      if (req.Ver is null)
      {
        if (TryReadInt(json, "ver") is { } rootVer)
        {
          req.Ver = rootVer;
        }
        else if (allowNestedFallback && TryReadInt(entityJson, "ver") is > 0 and var nestedVer)
        {
          req.Ver = nestedVer;
        }
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
