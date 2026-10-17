namespace DemoEcom.Data.Documents;

/// <summary>
/// What is to be purchased for a line of a customer order: the order, its line, the line's product, whose supplier and
/// purchase price the purchase order takes, and the quantity wanted of it.
/// </summary>
public sealed record EcomPurchase(EcomDoc Order, EcomDocLine Line, OrgProduct Product, decimal Qty);
