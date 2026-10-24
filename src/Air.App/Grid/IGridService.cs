namespace Platform.App.Grid;

public interface IGridService : IPlatformService
{
  PlatformDbContext DbContext { get; }
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) => BuildScreenAsync(servicePath, ct);
  Task<GridPageDto<object>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default);
  Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) => DeleteAsync(request, cancellationToken);
  Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default);
  Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default) => Task.FromResult<object?>(null);
  Task<object?> ReadRowAsync(long id, CancellationToken ct = default);
  IReadOnlyList<string>? GetReferenceLabelPaths() => null;
  string? GetRecordLabel(object? entity) => null;
  IReadOnlyList<GridCreateDescriptor> GetCreateForms() => [];
  new void AttachPlatformApp(PlatformAppService app);
  new Feature Feature { get; }
}
