namespace Platform.App.Form.Internals;

internal sealed class FormExecutionState<TEntity>
  where TEntity : class, IEntity, new()
{
  private Dictionary<string, object?>? _items;

  public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(StringComparer.Ordinal);
  public FormElements<TEntity>? LastElements { get; set; }
  public FormActions<TEntity>? CachedActions { get; set; }
  public FormParent<TEntity>? CachedParent { get; set; }
  public FormHooks<TEntity>? CachedHooks { get; set; }
  public TEntity? LastSavedEntity { get; set; }
  public List<MessageDto>? LastSaveWarnings { get; set; }
  public List<AffectedDto>? LastAffectedParents { get; set; }
  public object? PreloadedParent { get; set; }
  public long? PreloadedParentId { get; set; }
  public bool ParentRecordLoaded { get; set; }
  public (string? Service, long? ParentId, IReadOnlyList<ChildGridBuilder> Children)? CachedParentChildren { get; set; }

  public void ClearItems() => _items = null;

  public T? GetItem<T>(string key) =>
    _items is not null && _items.TryGetValue(key, out var value) && value is T typed
      ? typed
      : default;

  public void SetItem<T>(string key, T value) =>
    Items[key] = value;

  public void ResetSaveOutcome()
  {
    LastSavedEntity = null;
    LastSaveWarnings = null;
    LastAffectedParents = null;
    CachedActions = null;
    ClearItems();
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
