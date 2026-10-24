namespace Platform.Data.Contracts;

/// <summary>
/// Marks a JsonElement property that always carries a number in generated contracts.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WireNumberAttribute : Attribute;
