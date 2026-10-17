namespace Air.App.Contracts;

/// <summary>
/// Marks classes, records, and enums that are exported to the TypeScript contracts file (contracts.ts).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class UiContractAttribute : Attribute;
