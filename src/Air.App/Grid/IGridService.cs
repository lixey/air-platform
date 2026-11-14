namespace Platform.App.Grid;

public interface IGridService : IPlatformService
{
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) => BuildScreenAsync(servicePath, ct);
  Task<GridPageDto<RowDto>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default);
  internal Task<object?> LoadPickedEntityAsync(GridRequest request, long id, CancellationToken ct = default) => Task.FromResult<object?>(null);
  Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) => DeleteAsync(request, cancellationToken);
  Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default);
  Task<FlowDescriptionDto?> DescribeStatusFlowAsync(CancellationToken ct = default) => Task.FromResult<FlowDescriptionDto?>(null);
  Task<StatusMovePreviewDto> PreviewMoveStatusAsync(
    long? id,
    string transitionName,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    CancellationToken ct = default) =>
    throw new InvalidOperationException("Status flow is not configured.");
  internal bool HasStatusFlow() => false;
  Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default) => Task.FromResult(false);
  Task<IReadOnlyList<DeclaredActionDescriptorDto>> DescribeDeclaredActionsAsync(bool automationOnly = false, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<DeclaredActionDescriptorDto>>([]);
  Task<DeclaredActionPreviewDto?> PreviewDeclaredActionAsync(
    string actionKey,
    IReadOnlyList<long> selectedIds,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    bool requireAutomationOptIn = false,
    CancellationToken ct = default) =>
    Task.FromResult<DeclaredActionPreviewDto?>(null);
  Task<GridActionDto?> ExecuteDeclaredActionAsync(string actionKey, GridRequest request, JsonElement? body, CancellationToken ct = default) => Task.FromResult<GridActionDto?>(null);
  Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default);
  Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default) => Task.FromResult<object?>(null);
  Task<bool> BindParentAsync(string? parentService, long? parentId, CancellationToken ct = default) => Task.FromResult(true);
  Task<RowDto?> ReadRowAsync(long id, CancellationToken ct = default);
  IReadOnlyList<string>? GetReferenceLabelPaths() => null;
  IReadOnlyList<string>? GetLabelNavigationPaths() => null;
  string? GetRecordLabel(object? entity) => null;
  Task<string?> GetRecordLabelAsync(object? entity, CancellationToken ct = default) => Task.FromResult(GetRecordLabel(entity));
  IReadOnlyList<GridCreateDescriptor> GetCreateForms() => [];
  Task<IReadOnlyList<GridCreateDescriptor>> GetCreateFormsAsync(CancellationToken ct = default) => Task.FromResult(GetCreateForms());
  IReadOnlyList<GridCreateDescriptor> GetAgentForms() => GetCreateForms();
  Task<IReadOnlyList<GridCreateDescriptor>> GetDeclaredFormsAsync(CancellationToken ct = default) => Task.FromResult(GetAgentForms());
  bool CanDelete() => false;
  bool AllowsMultiDelete => false;
  void RegisterMetadata() { }
  Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    RegisterMetadata();
    return Task.CompletedTask;
  }

  internal PropertyInfo? GetBoundParentProperty() => null;
  bool CanView(GridRequest request, IUserContext? user) => ServiceEndpointsExtensions.CanView(Feature, user);
  bool CanLookup(GridRequest request, IUserContext? user) => CanView(request, user);
  IReadOnlyList<string> GetRoutableActionMethods(ModuleCatalog catalog) => [];
  new Feature Feature { get; }
}
