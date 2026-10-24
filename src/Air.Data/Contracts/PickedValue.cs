namespace Platform.Data.Contracts;

/// <summary>
/// A picked option: Id filters, Label is passed for UI rendering.
/// </summary>
public sealed class PickedValue
{
  public object? Id { get; set; }
  public string? Label { get; set; }
}
