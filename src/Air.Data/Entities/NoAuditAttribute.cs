namespace Platform.Data.Entities;

/// <summary>
/// Excludes an entity class or property from automatic audit trail capture (<see cref="AppPlatformChange"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class NoAuditAttribute : Attribute;
