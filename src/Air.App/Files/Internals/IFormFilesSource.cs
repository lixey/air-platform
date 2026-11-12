namespace Platform.App.Files.Internals;

internal interface IFormFilesSource
{
  int PageSize { get; }
  Task<FormFilesModel> BuildFormFilesAsync(
    string key,
    string? label,
    string? tab,
    string parentService,
    long? parentId,
    string look,
    CancellationToken cancellationToken
  );
  Task<GridActionDto> AttachFileAsync(
    long parentId,
    string fileName,
    string contentType,
    byte[] content,
    string? parentService = null,
    CancellationToken cancellationToken = default
  );
  Task<long?> FindExistingParentIdByHashAsync(
    string sha256,
    CancellationToken cancellationToken = default
  );
}
