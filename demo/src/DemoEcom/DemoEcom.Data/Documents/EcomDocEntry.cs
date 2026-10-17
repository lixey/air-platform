namespace DemoEcom.Data.Documents;

/// <summary>
/// A document entered on another, not added to any context: the document, its lines and the links that tie it to its
/// source, the document row first and then a row per line; a purchase order entered on the lines of several customer
/// orders has a document row per order, all of them first. The lines and the links name the document, and the links the
/// lines, by their navigations, so the three are added to a context together and saved in one flush.
/// </summary>
public sealed record EcomDocEntry(EcomDoc Doc, IReadOnlyList<EcomDocLine> Lines, IReadOnlyList<EcomDocLink> Links);
