using Air.App.Form.Contracts;

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
}
