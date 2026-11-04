namespace Platform.App.Form.Contracts;

public sealed class FormDraftResultDto
{
  public Dictionary<string, object?> OriginalValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public int? Ver { get; init; }
  public List<MessageDto> Messages { get; init; } = [];
}
