namespace Air.App.Mutations;

public sealed record MutationError(
  string Code,
  string Title,
  string? Path = null,
  long? RecordId = null,
  string? RecordLabel = null
)
{
  public MessageDto ToMessage(string type = "error") => new()
  {
    Path = Path,
    Code = Code,
    Type = type,
    Title = Title,
    RecordId = RecordId,
    RecordLabel = RecordLabel
  };
}
