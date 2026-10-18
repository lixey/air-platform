using System.Text.Json.Serialization;
using Air.Data.Contracts;

namespace Air.App.Grid.AddOns.Covers.Contracts;

[UiContract]
public sealed class GridCoverDto
{
  public long Id { get; set; }
  public required string Name { get; set; }
  public required string ContentType { get; set; }
  public required string Url { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ThumbUrl { get; set; }
}
