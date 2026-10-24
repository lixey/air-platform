namespace Platform.Agent.Internals;

internal sealed class PendingNewReference
{
  public string FieldPath { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string? FormService { get; set; }
  public bool IsMultiple { get; set; }
  public string? ProposalId { get; set; }
  public Dictionary<string, object?> ExtraValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
