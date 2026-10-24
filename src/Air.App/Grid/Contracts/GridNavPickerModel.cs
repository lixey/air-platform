namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavPickerModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  public string ReferenceService { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public int MinLength { get; set; } = 2;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool ReadsParent { get; set; }
}
