namespace Platform.Data.Contracts;

/// <summary>
/// Marks classes, records, structs, static vocabulary classes, and enums that are exported to TypeScript contracts (contracts.ts).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class UiContractAttribute : Attribute;
