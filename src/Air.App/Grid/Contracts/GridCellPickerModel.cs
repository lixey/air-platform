namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellPickerModel : CellModel
{
  public string ReferenceService { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public int MinLength { get; set; } = 2;
}
