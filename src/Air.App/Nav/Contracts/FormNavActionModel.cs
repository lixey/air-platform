namespace Platform.App.Nav.Contracts;

// Form Navigation Buttons
[UiContract]
public sealed record FormNavActionModel : NavButtonModel
{
  public string Service { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayConfirmModel? Confirm { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FormAskModel? Ask { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? UnavailableReasons { get; set; }
}
