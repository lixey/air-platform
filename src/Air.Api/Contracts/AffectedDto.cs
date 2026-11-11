namespace Platform.Api.Contracts;

[UiContract]
public sealed record AffectedDto
{
  public string Service { get; init; } = string.Empty;
  public long Id { get; init; }
  public int? PrevVer { get; init; }
  public int? Ver { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, object?>? Value { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, int>? Unavailable { get; init; }
}
