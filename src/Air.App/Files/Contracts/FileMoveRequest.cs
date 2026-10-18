using System.Text.Json.Serialization;
using Air.App.Grid;
using Air.Data.Contracts;

namespace Air.App.Files.Contracts;

[UiContract]
public sealed class FileMoveRequest : GridRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? BeforeId { get; set; }
}
