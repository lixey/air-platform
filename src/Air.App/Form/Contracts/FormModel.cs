namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public List<FormElementDto> Elements { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<FormTabDto>? Tabs { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<FormFilesModel>? Files { get; set; }

  public bool IsReadOnly { get; set; }
  public OverlayDirtyModel DirtyDialog { get; set; } = new();
  public MutationTextsDto MutationTexts { get; set; } = new();
}
