namespace Platform.Data.Internals;

internal sealed class MutationRunScope(Action onDispose) : IDisposable
{
  public Guid RunId { get; } = Guid.CreateVersion7();
  public int Depth { get; set; } = 1;
  public bool IsRollbackOnly { get; set; }
  public HashSet<object> VersionIncrementedEntities { get; } = new(ReferenceEqualityComparer.Instance);
  public Dictionary<object, MutationRunEntryState> JournalEntries { get; } = new(ReferenceEqualityComparer.Instance);

  public void Dispose() => onDispose();
}
