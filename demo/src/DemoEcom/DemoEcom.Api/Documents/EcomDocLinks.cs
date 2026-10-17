using System.Linq.Expressions;
using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

/// <summary>
/// What the link pages of every document type share: which links a page lists and how a link becomes its row.
/// </summary>
public static class EcomDocLinks
{
  /// <summary>The document rows: the links that name no line at either end.</summary>
  public static Expression<Func<EcomDocLink, bool>> Documents { get; } = link => link.SourceLineId == null && link.TargetLineId == null;

  /// <summary>The row of the page "Followed by": the link's target, the document entered on the one the page is of.</summary>
  public static Expression<Func<EcomDocLink, EcomDocLinkRow>> Follower { get; } = link => new EcomDocLinkRow
  {
    Id = link.Id,
    Number = link.TargetDoc.Number,
    Type = link.TargetDoc.DocType.Name,
    Date = link.TargetDoc.Date,
    Status = link.TargetDoc.DocStatus.Name,
    Qty = link.Qty,
    Amount = link.Amount
  };

  /// <summary>The row of the page "Entered on": the link's source, the document the one the page is of was entered on.</summary>
  public static Expression<Func<EcomDocLink, EcomDocLinkRow>> Basis { get; } = link => new EcomDocLinkRow
  {
    Id = link.Id,
    Number = link.SourceDoc.Number,
    Type = link.SourceDoc.DocType.Name,
    Date = link.SourceDoc.Date,
    Status = link.SourceDoc.DocStatus.Name,
    Qty = link.Qty,
    Amount = link.Amount
  };

  /// <summary>The hue the page "Followed by" paints a row's status in: that of the target's status.</summary>
  public static Expression<Func<EcomDocLink, string?>> FollowerHue { get; } = link => link.TargetDoc.DocStatus.Color.Handle;

  /// <summary>The hue the page "Entered on" paints a row's status in: that of the source's status.</summary>
  public static Expression<Func<EcomDocLink, string?>> BasisHue { get; } = link => link.SourceDoc.DocStatus.Color.Handle;
}
