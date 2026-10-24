namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record FormNavSaveOpenModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;
}
