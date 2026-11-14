namespace Platform.App.Form.Contracts;

public sealed class FormUnresolvedReferenceDto
{
  public required string FieldPath { get; init; }
  public string? ChildKey { get; init; }
  public int? RowIndex { get; init; }
  public bool IsMultiple { get; init; }
  public string? SymbolicRefKey { get; init; }
  public string? RawName { get; init; }
  public string? ReferenceService { get; init; }
  public string? CreateFormService { get; init; }
  public Dictionary<string, object?>? ExtraValues { get; init; }
}
