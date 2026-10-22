namespace Air.App.Form.Contracts;

[UiContract]
public sealed record FormFilesModel : PanelModel
{
  public required string Key { get; set; }
  public string? Label { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Tab { get; set; }

  public required string Service { get; set; }
  public required string ParentService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? ParentId { get; set; }

  [OneOf(typeof(FilesLook))]
  public string Look { get; set; } = FilesLook.Tiles;

  public required FileUploadLimitsDto Limits { get; set; }
  public bool CanUpload { get; set; }
  public bool CanDelete { get; set; }
  public bool CanSetCover { get; set; }
  public bool CanReadMetadata { get; set; }
}
