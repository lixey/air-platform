using Platform.Api.Files.Internals;

namespace Platform.App.Files.Internals;

internal static class FileUploadValidator
{
  private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

  public static MessageDto? ValidateHeader(
    string fileName,
    string? contentType,
    long fileLength,
    FileRulesAttribute rules,
    ILocalizer localizer,
    out string cleanedName,
    out string normalizedType)
  {
    cleanedName = string.Empty;
    normalizedType = "application/octet-stream";

    if (FileTypeTable.NameOf(fileName) is not { } validName)
    {
      return new MessageDto { Detail = localizer["FileNameRefused"], Code = "file.name", Type = MessageType.Error };
    }

    cleanedName = validName;
    if (rules.MaxBytes > 0 && fileLength > rules.MaxBytes)
    {
      return new MessageDto
      {
        Detail = localizer["FileTooLarge", Math.Round(rules.MaxBytes / (1024d * 1024d), 1)],
        Code = "file.size",
        Type = MessageType.Error
      };
    }

    var allowedTypes = FileTypeTable.TypesOf(rules.ContentTypes)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    normalizedType = FileTypeTable.Normalize(contentType) ?? "application/octet-stream";
    if (allowedTypes.Count > 0 && !allowedTypes.Contains(normalizedType))
    {
      return new MessageDto
      {
        Detail = localizer["FileTypeRefused", string.Join(", ", allowedTypes)],
        Code = "file.type",
        Type = MessageType.Error
      };
    }

    if (allowedTypes.Count > 0 && !FileTypeTable.HasExtension(normalizedType, cleanedName))
    {
      return new MessageDto
      {
        Detail = localizer["FileExtensionRefused", normalizedType],
        Code = "file.extension",
        Type = MessageType.Error
      };
    }

    return null;
  }

  public static MessageDto? ValidateMagicBytes(
    string normalizedType,
    ReadOnlySpan<byte> fileBytes,
    ILocalizer localizer)
  {
    var headSpan = fileBytes[..Math.Min(fileBytes.Length, 12)];
    if (!FileTypeTable.StartsAsDeclared(normalizedType, headSpan) ||
        FileTypeTable.IsZipDocument(normalizedType) && !FileTypeTable.StartsAsZip(headSpan))
    {
      return new MessageDto
      {
        Detail = localizer["FileContentRefused", normalizedType],
        Code = "file.content",
        Type = MessageType.Error
      };
    }

    return null;
  }

  public static bool TryNormalizeMetadata(
    string? rawMetadata,
    ILocalizer localizer,
    out string? normalizedMetadata,
    out MessageDto? error)
  {
    normalizedMetadata = null;
    error = null;
    if (string.IsNullOrWhiteSpace(rawMetadata))
    {
      return true;
    }

    if (rawMetadata.Length > 16384)
    {
      error = new MessageDto
      {
        Detail = localizer["FileMetadataInvalid"],
        Path = "metadata",
        Code = "file.metadata",
        Type = MessageType.Error
      };
      return false;
    }

    try
    {
      var parsedMeta = JsonSerializer.Deserialize<FileMetadataDto>(rawMetadata, WebJsonOptions);
      if (parsedMeta != null)
      {
        normalizedMetadata = JsonSerializer.Serialize(parsedMeta, WebJsonOptions);
      }

      return true;
    }
    catch
    {
      error = new MessageDto
      {
        Detail = localizer["FileMetadataInvalid"],
        Path = "metadata",
        Code = "file.metadata",
        Type = MessageType.Error
      };
      return false;
    }
  }
}
