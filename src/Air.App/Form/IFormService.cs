namespace Platform.App.Form;

public interface IFormService : IPlatformService
{
  PlatformDbContext DbContext { get; }
  string Service { get; }
  OverlayDto Overlay(string service);
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
  Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, int? ver = null, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(long id, string path, JsonElement value, FormRequest request, Func<CancellationToken, Task<object?>> readRow, CancellationToken ct = default);
  Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default) => Task.FromResult<object?>(null);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, ScreenDto screen, CancellationToken ct = default);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, CancellationToken ct = default);
  Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default) => Task.FromResult<Dictionary<string, int>?>(null);
  Task<(IReadOnlyList<FormElementDto> Elements, IReadOnlyList<ChildGridBuilder> Children)> GetElementsMetadataAsync(CancellationToken ct = default);
  string GetTitle() => string.Empty;
  string GetRecordCaption(object? entity, long? id) => string.Empty;
  IReadOnlyList<MessageDto>? GetLastSaveWarnings() => null;
  new void AttachPlatformApp(PlatformAppService app);
  new Feature Feature { get; }
}
