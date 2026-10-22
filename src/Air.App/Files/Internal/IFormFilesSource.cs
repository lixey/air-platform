namespace Air.App.Files.Internal;

public interface IFormFilesSource
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
  Task<long?> AttachFileAsync(
    long parentId,
    string fileName,
    string contentType,
    byte[] content,
    CancellationToken cancellationToken = default
  );
}
