using System.Text.Json.Serialization;

namespace Air.Data.Contracts;

/// <summary>
/// A page of rows returned to the client. Total is omitted when hasMore is true to avoid redundant COUNT(*).
/// </summary>
public class Page<TRow>
{
  public required List<TRow> Rows { get; init; }
  public int? Total { get; init; }

  [JsonPropertyName("page")]
  public required int Number { get; init; }

  [JsonPropertyName("pageSize")]
  public required int Size { get; init; }

  public required bool HasMore { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<TRow>? TopRows { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<TRow>? BottomRows { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, object?>? Forms { get; init; }
}
