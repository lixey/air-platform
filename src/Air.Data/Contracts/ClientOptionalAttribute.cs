namespace Air.Data.Contracts;

/// <summary>
/// Marks what a client may leave out although the server always writes it:
/// on a class, members are optional on wire; on property, marks it optional in generated contracts.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class ClientOptionalAttribute : Attribute;
