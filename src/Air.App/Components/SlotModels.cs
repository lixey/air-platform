namespace Air.App.Components;

[UiContract]
public abstract record PanelModel;

[UiContract]
public abstract record NavElementModel
{
  public string Key { get; set; } = string.Empty;
}

[UiContract]
public abstract record CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ColorPath { get; init; }
}

[UiContract]
public abstract record HeaderModel;

[UiContract]
public abstract record FormElementModel;
