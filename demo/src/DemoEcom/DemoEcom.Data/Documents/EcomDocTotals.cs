namespace DemoEcom.Data.Documents;

/// <summary>
/// The amounts of a document: a line's of its quantity, price and discount, and the document's of its lines. A payment
/// has no lines: its amount is the one its header holds.
/// </summary>
public static class EcomDocTotals
{
  /// <summary>
  /// Whether a document of the type has lines, whose totals it keeps: every type but the two payments, whose amount is
  /// entered on the header and which a posted status asks no line of.
  /// </summary>
  public static bool HasLines(long docTypeId) => !EcomMoney.Moves(docTypeId);

  /// <summary>
  /// The amount of a line: the quantity times the price less the discount in percent, rounded to two decimals, a half
  /// away from zero.
  /// </summary>
  public static decimal LineAmount(decimal qty, decimal price, decimal discountPercent) =>
    Math.Round(qty * price * (100m - discountPercent) / 100m, 2, MidpointRounding.AwayFromZero);

  /// <summary>
  /// Sets the quantity and the amount of <paramref name="doc"/> to the sums over its lines, the stored ones and the ones
  /// this context tracks together: a line added and not saved yet counts, a line marked deleted does not, a changed line
  /// counts as changed. It stages only. A document of a type without lines (<see cref="HasLines"/>) is left as it is,
  /// and nothing is read for it.
  /// </summary>
  public static async Task ApplyAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    if (HasLines(doc.DocTypeId))
    {
      Apply(doc, await LinesAsync(db, doc, cancellationToken));
    }
  }

  /// <summary>
  /// Sets the quantity and the amount of <paramref name="doc"/> to the sums over <paramref name="lines"/>, its lines as
  /// the caller already holds them. It changes the document alone, and a document of a type without lines
  /// (<see cref="HasLines"/>) not at all: a payment keeps the amount its header holds.
  /// </summary>
  public static void Apply(EcomDoc doc, IReadOnlyCollection<EcomDocLine> lines)
  {
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(lines);
    if (!HasLines(doc.DocTypeId))
    {
      return;
    }

    doc.Qty = lines.Sum(line => line.Qty);
    doc.Amount = lines.Sum(line => line.Amount);
  }

  /// <summary>
  /// The lines of <paramref name="doc"/> as this context's save would leave them: the stored ones are loaded into the
  /// context, where a line it already tracks keeps its changes, then the tracked ones are read without those marked
  /// deleted. A document not saved yet has only the lines this context tracks for it.
  /// </summary>
  internal static async Task<List<EcomDocLine>> LinesAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    if (doc.Id != 0 && db.Entry(doc).State != EntityState.Added)
    {
      await db.EcomDocLine.Where(line => line.DocId == doc.Id).LoadAsync(cancellationToken);
    }

    return Tracked(db, doc);
  }

  /// <summary>
  /// The lines of <paramref name="doc"/> this context tracks, without those marked deleted and without a read: all of
  /// its lines once the stored ones were loaded.
  /// </summary>
  internal static List<EcomDocLine> Tracked(EcomDbContext db, EcomDoc doc) =>
    [.. db.EcomDocLine.Local.Where(line => db.Entry(line).State != EntityState.Deleted && (line.Doc == doc || doc.Id != 0 && line.DocId == doc.Id))];
}
