namespace DemoEcom.Migrations.Seeds.Internals;

internal sealed record EcomSeedWritten(
  EcomDoc Doc,
  IReadOnlyList<EcomDocLine> Lines,
  IReadOnlyList<EcomStockMove> Moves,
  IReadOnlyList<EcomDocLink> Links,
  IReadOnlyList<EcomMoneyMove> MoneyMoves,
  IReadOnlyList<EcomDocMove> History
);
