namespace Platform.App.Components.Contracts;

[UiContract]
public abstract record NavElementModel : IUiSlotModel
{
  public string Key { get; set; } = string.Empty;
}
