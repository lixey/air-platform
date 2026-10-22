namespace Air.App.Files.Contracts;

[UiContract]
public sealed record GridNavUploadModel : NavButtonModel
{
  public required FileUploadLimitsDto Limits { get; set; }
  public bool CanReadMetadata { get; set; }
}
