namespace Platform.App.Form.Contracts;

public sealed class FormCompositeDraftDto
{
  public long? RecordId { get; init; }
  public int? Ver { get; init; }
  public string? RecordCaption { get; init; }
  public Dictionary<string, object?> OriginalValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public HashSet<string> ExplicitFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public IReadOnlyList<FormElementDto> Elements { get; init; } = [];
  public List<FormChildDraftGroupDto> Children { get; init; } = [];
  public List<FormUnresolvedReferenceDto> UnresolvedReferences { get; init; } = [];
  public Dictionary<string, List<Dictionary<string, object?>>> Dictionaries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public List<MessageDto> Messages { get; init; } = [];
}
