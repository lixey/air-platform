namespace Platform.Data.Entities;

/// <summary>
/// Marks an entity class as non-restorable via the change journal (ChangeGridService.Restore).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class NoRestoreAttribute : Attribute;
