namespace Platform.App.Form.Internals;

internal sealed class FormExecutionState<TEntity>
  where TEntity : class, IEntity, new()
{
  public FormService<TEntity>.Elements? LastElements { get; set; }
  public FormService<TEntity>.Parent? CachedParent { get; set; }
  public FormService<TEntity>.Hooks? CachedHooks { get; set; }
  public TEntity? LastSavedEntity { get; set; }
  public List<MessageDto>? LastSaveWarnings { get; set; }
  public List<AffectedDto>? LastAffectedParents { get; set; }
  public object? PreloadedParent { get; set; }
  public long? PreloadedParentId { get; set; }
  public bool ParentRecordLoaded { get; set; }
  public (string? Service, long? ParentId, IReadOnlyList<ChildGridBuilder> Children)? CachedParentChildren { get; set; }

  public void ResetSaveOutcome()
  {
    LastSavedEntity = null;
    LastSaveWarnings = null;
    LastAffectedParents = null;
  }

  public void CacheParentScope(
    object? preloaded,
    long? parentId,
    string? requestParentService,
    string? boundParentService,
    IReadOnlyList<ChildGridBuilder>? parentChildren,
    bool resetLoadedWhenMissing)
  {
    PreloadedParent = preloaded;
    PreloadedParentId = parentId;
    if (parentId is > 0)
    {
      ParentRecordLoaded = true;
    }
    else if (resetLoadedWhenMissing)
    {
      ParentRecordLoaded = false;
    }

    var effectiveService = !string.IsNullOrEmpty(requestParentService) ? requestParentService : boundParentService;
    if (!string.IsNullOrEmpty(effectiveService) && parentChildren != null)
    {
      CachedParentChildren = (effectiveService, parentId, parentChildren);
    }
  }
}
