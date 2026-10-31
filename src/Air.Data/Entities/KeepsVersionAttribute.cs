namespace Platform.Data.Entities;

/// <summary>
/// Marks a property whose mutation does not increment <see cref="IHasVersion.Ver"/> or update <see cref="IHasTimestamps.UpdatedAt"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class KeepsVersionAttribute : Attribute
{
  public bool IsConcurrencyToken { get; init; } = true;
}
