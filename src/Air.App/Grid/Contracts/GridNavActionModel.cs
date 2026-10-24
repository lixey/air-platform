namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavActionModel : NavButtonModel
{
  public string Service { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public bool NeedsSelection { get; set; } = true;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsSingle { get; set; }

  public bool IsDownload { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayConfirmModel? Confirm { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FormAskModel? Ask { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? UnavailableReasons { get; set; }
}
