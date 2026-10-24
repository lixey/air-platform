namespace Platform.Data.Entities;

/// <summary>
/// Marks a property (e.g. a status column) whose mutation acts as its own concurrency token
/// and does not increment <see cref="IHasVersion.Ver"/> or update <see cref="IHasTimestamps.UpdatedAt"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class KeepsVersionAttribute : Attribute;
