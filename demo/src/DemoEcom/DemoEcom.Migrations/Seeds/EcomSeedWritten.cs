using DemoEcom.Data.Models;

namespace DemoEcom.Migrations.Seeds;

internal sealed record EcomSeedWritten(
  EcomDoc Doc,
  IReadOnlyList<EcomDocLine> Lines,
  IReadOnlyList<EcomStockMove> Moves,
  IReadOnlyList<EcomDocLink> Links,
  IReadOnlyList<EcomMoneyMove> MoneyMoves,
  IReadOnlyList<EcomDocMove> History
);
