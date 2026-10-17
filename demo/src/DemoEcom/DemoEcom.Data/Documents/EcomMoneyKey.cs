namespace DemoEcom.Data.Documents;

/// <summary>What a money balance is kept of: a money account of an organisation.</summary>
public readonly record struct EcomMoneyKey(long OrganizationId, long MoneyAccountId);
