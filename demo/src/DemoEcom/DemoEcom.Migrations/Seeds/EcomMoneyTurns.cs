namespace DemoEcom.Migrations.Seeds;

internal sealed class EcomMoneyTurns
{
  private readonly Dictionary<long, int> _orders = [];
  private int _invoices;

  public int NextOrder(long paymentTermId)
  {
    var place = _orders.GetValueOrDefault(paymentTermId);
    _orders[paymentTermId] = place + 1;
    return place;
  }

  public int NextInvoice() => _invoices++;
}
