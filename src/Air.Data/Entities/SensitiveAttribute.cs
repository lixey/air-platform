namespace Platform.Data.Entities;

/// <summary>
/// Marks a property (e.g. password hash, secret token) that must never be filtered, sorted, searched or queried.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class SensitiveAttribute : Attribute;
