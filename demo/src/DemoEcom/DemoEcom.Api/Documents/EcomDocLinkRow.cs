namespace DemoEcom.Documents;

/// <summary>
/// A link as the pages "Followed by" and "Entered on" show it: the document at the other end, by its number, the name of
/// its type, its date and the name of its status, and the quantity and the amount the link covers.
/// </summary>
public sealed class EcomDocLinkRow
{
  public long Id { get; set; }

  public string? Number { get; set; }

  public string? Type { get; set; }

  public DateOnly Date { get; set; }

  public string? Status { get; set; }

  public decimal? Qty { get; set; }

  public decimal? Amount { get; set; }
}
