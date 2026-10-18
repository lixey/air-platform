using Air.Data.Contracts;

namespace Air.App.Files.Contracts;

[UiContract]
public sealed class GridUploadRequest
{
  public string? ParentService { get; set; }
  public long? ParentId { get; set; }
  public long? AfterId { get; set; }
  public string? Metadata { get; set; }
}
