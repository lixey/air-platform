namespace Platform.App.Form;

public interface IFormService : IPlatformService
{
  PlatformDbContext DbContext { get; }
  internal PlatformAppService? HostApp => null;
  string Service { get; }
  bool IsFormReadOnly { get; }
  OverlayDto Overlay(string service);
  Task<OverlayDto> OverlayAsync(string service, CancellationToken ct = default) => Task.FromResult(Overlay(service));
  Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, (long?)null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default);
  void SetRequest(FormRequest request);
  Task<object?> LoadParentRecordAsync(long id, CancellationToken ct = default);
  Task<FormDataDto> BuildFormDataAsync(object? entity, ScreenDto? screen = null, string? outcome = null, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, FormRequest? request, CancellationToken ct = default);
  Task<FormSaveRecordDto> SaveRecordAsync(FormRequest request, CancellationToken ct = default) =>
    throw new NotSupportedException();
  Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, int? ver = null, CancellationToken ct = default);
  Task<FlowDescriptionDto?> DescribeStatusFlowAsync(CancellationToken ct = default) => Task.FromResult<FlowDescriptionDto?>(null);
  Task<StatusMovePreviewDto> PreviewMoveStatusAsync(
    long? id,
    string transitionName,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    CancellationToken ct = default) =>
    throw new InvalidOperationException("Status flow is not configured.");
  internal Task<bool> HasStatusFlowAsync(CancellationToken ct = default) => Task.FromResult(false);
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
  Task<FormActionDto?> ExecuteDeclaredActionAsync(string actionKey, FormRequest request, JsonElement? body, CancellationToken ct = default) => Task.FromResult<FormActionDto?>(null);
  Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<RowDto?>> readRow,
    Func<CancellationToken, Task<bool>>? authorizeRow = null,
    CancellationToken ct = default);
  Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default) => Task.FromResult<object?>(null);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, ScreenDto screen, CancellationToken ct = default);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, CancellationToken ct = default);
  Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default) => Task.FromResult<Dictionary<string, int>?>(null);
  Task<(IReadOnlyList<FormElementDto> Elements, IReadOnlyList<ChildGridBuilder> Children)> GetElementsMetadataAsync(CancellationToken ct = default);
  Task<FormDraftResultDto> PrepareDraftAsync(
    long? recordId = null,
    IReadOnlyDictionary<string, object?>? overrides = null,
    ISet<string>? pendingReferencePaths = null,
    long? parentId = null,
    string? parentService = null,
    string? triggerField = null,
    CancellationToken ct = default);
  Task<FormCompositeDraftDto> PrepareCompositeDraftAsync(
    FormCompositeDraftRequest request,
    CancellationToken ct = default) =>
    throw new NotSupportedException();
  Task<FormChildBatchResultDto> ApplyChildRowsAsync(
    long parentId,
    bool isParentEdit,
    IReadOnlyDictionary<string, object?>? parentValues,
    IReadOnlyList<FormChildBatchGroupDto> groups,
    Func<Feature, Feature, bool>? childPermissionFilter = null,
    CancellationToken ct = default) =>
    Task.FromResult(new FormChildBatchResultDto());
  bool HasCustomRecalculate => false;
  Task<bool> NeedsParentRecalculationAsync(CancellationToken ct = default) => Task.FromResult(HasCustomRecalculate);
  Task RecalculateParentAsync(IReadOnlyList<long> parentIds, CancellationToken ct = default) => Task.CompletedTask;
  Task RegisterMetadataAsync(CancellationToken ct = default) => GetElementsMetadataAsync(ct);
  string GetTitle() => string.Empty;
  string GetRecordCaption(object? entity, long? id) => string.Empty;
  Task<string> GetRecordCaptionAsync(object? entity, long? id, CancellationToken ct = default) => Task.FromResult(GetRecordCaption(entity, id));
  IReadOnlyList<MessageDto>? GetLastSaveWarnings() => null;
  internal Task<IReadOnlyList<(GridNavActionModel Model, bool IsInMenu, bool IsAllowedInReadOnly, IReadOnlyList<RecordAvailability> Offers)>> BuildInheritedGridActionsAsync(
    string gridServicePath,
    long? parentId,
    string? parentService,
    bool isGridReadOnly,
    bool isParentReadOnly = false,
    CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<(GridNavActionModel, bool, bool, IReadOnlyList<RecordAvailability>)>>([]);
  internal Task<IReadOnlyList<(string Key, IReadOnlyList<RecordAvailability> Offers)>> GetActionOffersAsync(
    long? parentId,
    string? parentService,
    CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<(string, IReadOnlyList<RecordAvailability>)>>([]);
  internal Task<bool> IsActionPermittedForGridAsync(
    string actionKey,
    bool isGridReadOnly,
    bool isParentReadOnly = false,
    CancellationToken ct = default) =>
    Task.FromResult(false);
  internal void ValidateInheritedGridActions(Type gridType) { }
  IReadOnlyList<string> GetRoutableActionMethods(ModuleCatalog catalog) => [];
  new Feature Feature { get; }
}
