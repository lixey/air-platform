namespace DemoEcom.Migrations.Seeds;

internal sealed record EcomSeedStep(DateOnly Date, int Rank, Func<IReadOnlyList<EcomSeedWritten>> Write)
{
  public const int Capital = 0;
  public const int Stock = 1;
  public const int Order = 2;
  public const int Return = 3;
}
