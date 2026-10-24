namespace Platform.Data.Entities;

/// <summary>
/// Excludes an entity class or a specific property from automatic change journaling (AppPlatformChange).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NoAuditAttribute : Attribute;
