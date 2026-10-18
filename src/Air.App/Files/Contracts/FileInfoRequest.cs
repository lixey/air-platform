using Air.App.Grid;
using Air.Data.Contracts;

namespace Air.App.Files.Contracts;

[UiContract]
public sealed class FileInfoRequest : GridRequest
{
  public string? Name { get; set; }
  public string? Description { get; set; }
}
