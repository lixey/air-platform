namespace DemoEcom.MoneyMove;

public sealed class EcomMoneyMoveRow
{
  public long Id { get; set; }

  public DateOnly Date { get; set; }

  public string? Number { get; set; }

  public string? Type { get; set; }

  public Reference? Organization { get; set; }

  public Reference? MoneyAccount { get; set; }

  public decimal Amount { get; set; }
}
