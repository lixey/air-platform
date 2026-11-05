namespace DemoEcom.Documents;

/// <summary>
/// What the link pages of every document type share: which links a page lists and how a link colors its status.
/// </summary>
public static class EcomDocLinks
{
  /// <summary>The document rows: the links that name no line at either end.</summary>
  public static Expression<Func<EcomDocLink, bool>> Documents { get; } = link => link.SourceLineId == null && link.TargetLineId == null;

  /// <summary>The hue the page "Followed by" paints a row's status in: that of the target's status.</summary>
  public static Expression<Func<EcomDocLink, string?>> FollowerHue { get; } = link => link.TargetDoc.DocStatus.Color.Handle;

  /// <summary>The hue the page "Entered on" paints a row's status in: that of the source's status.</summary>
  public static Expression<Func<EcomDocLink, string?>> BasisHue { get; } = link => link.SourceDoc.DocStatus.Color.Handle;
}
