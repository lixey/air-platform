namespace Platform.Data.Internals;

internal sealed class MutationRunScope(Action onDispose) : IDisposable
{
  public Guid RunId { get; } = Guid.CreateVersion7();
  public int Depth { get; set; } = 1;
  public bool IsRollbackOnly { get; set; }
  public HashSet<object> VersionIncrementedEntities { get; } = new(ReferenceEqualityComparer.Instance);
  public HashSet<(Type EntityType, long Id)> VersionIncrementedKeys { get; } = [];
  public Dictionary<(Type EntityType, long Id), int> InitialVersions { get; } = [];
  public List<MutationRunEntryState> JournalEntries { get; } = [];
  public Dictionary<(Type EntityType, long Id), MutationRunEntryState> JournalEntriesByKey { get; } = [];
  public Dictionary<object, MutationRunEntryState> JournalEntriesByRef { get; } = new(ReferenceEqualityComparer.Instance);

  public void ClearJournal()
  {
    JournalEntries.Clear();
    JournalEntriesByKey.Clear();
    JournalEntriesByRef.Clear();
  }

  public void Dispose() => onDispose();
}
