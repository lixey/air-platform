namespace Platform.Agent.Internals;

internal sealed class ChildRowBuffer(Dictionary<string, object?> baseValues, long? itemRowId)
{
  public long? ItemRowId { get; } = itemRowId;

  public Dictionary<string, object?> Values { get; } = InitializeValues(baseValues, itemRowId);

  public Dictionary<string, string> RefLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

  public HashSet<string> ExplicitFields { get; } = new(StringComparer.OrdinalIgnoreCase);

  public List<PendingNewReference> PendingRefs { get; } = [];

  private static Dictionary<string, object?> InitializeValues(Dictionary<string, object?> baseValues, long? itemRowId)
  {
    var dict = new Dictionary<string, object?>(baseValues, StringComparer.OrdinalIgnoreCase);
    if (itemRowId is > 0)
    {
      dict["id"] = itemRowId.Value;
    }

    return dict;
  }
}
