namespace Platform.App.Form.Contracts;

public sealed class FormChildDraftRowDto
{
  public long? RecordId { get; init; }
  public int? Ver { get; init; }
  public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public HashSet<string> ExplicitFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public Dictionary<string, string> ReferenceLabels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public List<MessageDto> Messages { get; init; } = [];
}
