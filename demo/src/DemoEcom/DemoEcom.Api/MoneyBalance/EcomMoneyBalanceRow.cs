namespace DemoEcom.MoneyBalance;

public sealed class EcomMoneyBalanceRow
{
  public long Id { get; set; }

  public Reference? Organization { get; set; }

  public Reference? MoneyAccount { get; set; }

  public bool IsCash { get; set; }

  public decimal Amount { get; set; }
}
