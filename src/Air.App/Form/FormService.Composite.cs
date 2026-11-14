using Platform.App.Form.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual async Task<FormSaveRecordDto> SaveRecordAsync(FormRequest request, CancellationToken ct = default)
  {
    var json = request.Value ?? JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    var savedObj = await SaveAsync(json, request, ct);
    var entity = State.LastSavedEntity ?? savedObj as TEntity;
    var savedId = entity?.Id
      ?? (savedObj as IEntity)?.Id
      ?? (savedObj as FormDataDto)?.Id
      ?? request.Id
      ?? 0;

    if (savedId <= 0 && savedObj is FormDataDto failedDto)
    {
      return new FormSaveRecordDto
      {
        Outcome = failedDto.Outcome ?? MutationOutcome.Refused,
        Id = 0,
        Messages = failedDto.Messages ?? [],
        Warnings = failedDto.Warnings ?? []
      };
    }

    return new FormSaveRecordDto
    {
      Outcome = MutationOutcome.Committed,
      Id = savedId,
      Ver = (entity as IHasVersion)?.Ver,
      Entity = entity ?? savedObj,
      Warnings = State.LastSaveWarnings != null ? [.. State.LastSaveWarnings] : [],
      Affected = State.LastAffectedParents
    };
  }

  public virtual Task<FormCompositeDraftDto> PrepareCompositeDraftAsync(
    FormCompositeDraftRequest request,
    CancellationToken ct = default) =>
    FormCompositeDraftBuilder.BuildAsync<TEntity>(this, GetType(), db, PlatformApp, L, request, ct);

  public virtual async Task<FormChildBatchResultDto> ApplyChildRowsAsync(
    long parentId,
    bool isParentEdit,
    IReadOnlyDictionary<string, object?>? parentValues,
    IReadOnlyList<FormChildBatchGroupDto> groups,
    Func<Feature, Feature, bool>? childPermissionFilter = null,
    CancellationToken ct = default)
  {
    var (_, declaredChildren) = await GetElementsMetadataAsync(ct);
    return await FormChildBatchSaver.ApplyAsync(
      this,
      GetType(),
      db,
      PlatformApp,
      L,
      parentId,
      isParentEdit,
      parentValues,
      declaredChildren,
      groups,
      childPermissionFilter,
      ct);
  }
}
