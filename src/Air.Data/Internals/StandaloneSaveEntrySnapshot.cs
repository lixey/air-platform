using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Platform.Data.Internals;

internal sealed class StandaloneSaveEntrySnapshot(
  EntityEntry entry,
  EntityState priorState,
  PropertyValues? originalValues,
  string[] modifiedProperties,
  (string Name, object? PriorValue, bool WasTemporary)[] generatedDefaults)
{
  public static List<StandaloneSaveEntrySnapshot> Capture(ChangeTracker tracker)
  {
    var snapshots = new List<StandaloneSaveEntrySnapshot>();
    foreach (var entry in tracker.Entries())
    {
      if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
      {
        continue;
      }

      var state = entry.State;
      var originals = state is EntityState.Modified or EntityState.Deleted
        ? entry.OriginalValues.Clone()
        : null;
      var modified = state == EntityState.Modified
        ? entry.Properties.Where(static p => p.IsModified).Select(static p => p.Metadata.Name).ToArray()
        : [];
      var defaults = state == EntityState.Added
        ? entry.Properties
          .Where(static p =>
            p.Metadata.ValueGenerated == ValueGenerated.OnAdd &&
            (p.IsTemporary ||
             Equals(p.CurrentValue, p.Metadata.ClrType.IsValueType ? Activator.CreateInstance(p.Metadata.ClrType) : null)))
          .Select(static p => (p.Metadata.Name, p.CurrentValue, p.IsTemporary))
          .ToArray()
        : [];

      snapshots.Add(new StandaloneSaveEntrySnapshot(entry, state, originals, modified, defaults));
    }

    return snapshots;
  }

  public static void Restore(List<StandaloneSaveEntrySnapshot> snapshots, bool resetGeneratedKeys)
  {
    foreach (var snap in snapshots)
    {
      switch (snap.PriorState)
      {
        case EntityState.Added:
          snap.Entry.State = EntityState.Added;
          if (resetGeneratedKeys)
          {
            foreach (var (name, priorValue, wasTemporary) in snap.GeneratedDefaults)
            {
              var prop = snap.Entry.Property(name);
              prop.CurrentValue = priorValue;
              prop.IsTemporary = wasTemporary;
            }
          }

          break;

        case EntityState.Modified:
          snap.Entry.State = EntityState.Unchanged;
          snap.Entry.OriginalValues.SetValues(snap.OriginalValues!);
          foreach (var prop in snap.ModifiedProperties)
          {
            snap.Entry.Property(prop).IsModified = true;
          }

          break;

        case EntityState.Deleted:
          snap.Entry.State = EntityState.Unchanged;
          snap.Entry.OriginalValues.SetValues(snap.OriginalValues!);
          snap.Entry.State = EntityState.Deleted;
          break;
      }
    }
  }

  private EntityEntry Entry { get; } = entry;
  private EntityState PriorState { get; } = priorState;
  private PropertyValues? OriginalValues { get; } = originalValues;
  private string[] ModifiedProperties { get; } = modifiedProperties;
  private (string Name, object? PriorValue, bool WasTemporary)[] GeneratedDefaults { get; } = generatedDefaults;
}
