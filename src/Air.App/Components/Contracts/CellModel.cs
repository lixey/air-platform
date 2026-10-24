namespace Platform.App.Components.Contracts;

[UiContract]
public abstract record CellModel : IUiSlotModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ColorPath { get; set; }
}
