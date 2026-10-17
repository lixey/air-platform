namespace DemoEcom.Data.Documents;

/// <summary>
/// Why a change of a document's status, or the entering of a document on another, is refused, as data: the code names
/// the reason, and the line the one it is about when it is about a line. A refusal by the stock names the line's product
/// as its article and its name, and how much of it is missing; a line over its source's open quantity names its product
/// the same way, and by how much it is over. A refusal by the money names the money account by its name, and how much
/// it is short of. A warehouse or a money account of another organisation is refused for the document as a whole, with
/// no line, product or amount. The screens give each code its text.
/// </summary>
public sealed record EcomDocRefusal(string Code, long? LineId = null, string? Product = null, decimal? Short = null, string? Account = null)
{
  /// <summary>A posted status is asked of a document without a line.</summary>
  public const string NoLines = "doc.noLines";

  /// <summary>The status moves stock, and the document names no warehouse for it.</summary>
  public const string NoWarehouse = "doc.noWarehouse";

  /// <summary>
  /// The status moves stock, and a warehouse a move would name is not one of the document's organisation: the document was
  /// written past the forms, whose references offer the organisation's warehouses alone.
  /// </summary>
  public const string ForeignWarehouse = "doc.foreignWarehouse";

  /// <summary>
  /// The status would take more of a product than its warehouse has free: an issue or a transfer over the free stock, or
  /// a receipt taken back when its goods are gone.
  /// </summary>
  public const string StockShort = "stock.short";

  /// <summary>The status moves money, and the document names no money account for it.</summary>
  public const string NoAccount = "doc.noAccount";

  /// <summary>
  /// The status moves money, and the payment's money account is not one of its organisation: the payment was written past
  /// the forms, whose reference offers the organisation's accounts alone.
  /// </summary>
  public const string ForeignAccount = "doc.foreignAccount";

  /// <summary>
  /// The status would take more money from an account than it holds: an outgoing payment over the account, or an incoming
  /// one taken back when its money is spent. The refusal names the account and what it is short of.
  /// </summary>
  public const string MoneyShort = "money.short";

  /// <summary>
  /// The document would leave the posted statuses while documents entered on it are not canceled: its followers hold it.
  /// A purchase order entered on a customer order's lines holds nothing.
  /// </summary>
  public const string Followed = "doc.followed";

  /// <summary>
  /// An entered line takes more of its source's line than the line has open; the refusal of a status names the line's
  /// product and by how much it is over.
  /// </summary>
  public const string OverSource = "doc.overSource";

  /// <summary>A document is to be entered on one that is not in a posted status.</summary>
  public const string SourceNotPosted = "doc.sourceNotPosted";

  /// <summary>A document is to be entered on one whose lines have nothing open to enter.</summary>
  public const string NothingOpen = "doc.nothingOpen";

  /// <summary>An entered line names another product than its source's line.</summary>
  public const string SourceProduct = "doc.sourceProduct";

  /// <summary>Purchase orders are to be created for order lines none of which needs a purchase.</summary>
  public const string NothingToPurchase = "purchase.nothing";
}
