namespace Platform.Data.Entities;

/// <summary>
/// Excludes an entity class or derived property from automatic audit trail capture (<see cref="AppPlatformChange"/>)
/// and prevents property-only updates from incrementing <see cref="IHasVersion.Ver"/> or <see cref="IHasTimestamps.UpdatedAt"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class NoAuditAttribute : Attribute;
