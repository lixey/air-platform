namespace Platform.App.Grid.Internals;

internal sealed class GridExecutionState<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private Dictionary<string, object?>? _items;

  public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(StringComparer.Ordinal);
  public GridParent<TEntity>? CachedParent { get; set; }
  public GridHooks<TEntity>? CachedHooks { get; set; }

  public void ClearItems() => _items = null;

  public T? GetItem<T>(string key) =>
    _items is not null && _items.TryGetValue(key, out var value) && value is T typed
      ? typed
      : default;

  public void SetItem<T>(string key, T value) =>
    Items[key] = value;
  public object? PreloadedParent { get; set; }
  public long? PreloadedParentId { get; set; }
  public (string? ParentService, long? ParentId, bool Allowed, object? Preloaded)? CachedParentValidation { get; set; }
  public (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? CachedParentChildren { get; set; }
  public (string? ParentService, long? ParentId, Columns<TEntity, TRow> Value)? CachedColumns { get; set; }
  public (string? ParentService, long? ParentId, GridQuery<TEntity, TRow> Value)? CachedQuery { get; set; }
  public (string? ParentService, long? ParentId, GridRestrictions<TEntity> Value)? CachedRestrictions { get; set; }
  public (string? ParentService, long? ParentId, string ServicePath, GridNav Value)? CachedNav { get; set; }
  public (string? ParentService, long? ParentId, string SelectedKey, GridActions<TEntity> Value)? CachedActions { get; set; }
  public Dictionary<string, OverlayDto> CachedFormOverlays { get; } = new(StringComparer.OrdinalIgnoreCase);

  public void CacheParentScope(
    bool allowed,
    object? preloaded,
    long? parentId,
    string? requestParentService,
    string? boundParentService,
    IReadOnlyList<Form.ChildGridBuilder>? parentChildren)
  {
    PreloadedParent = preloaded;
    PreloadedParentId = parentId;
    CachedParentValidation = (requestParentService, parentId, allowed, preloaded);
    var effectiveService = !string.IsNullOrEmpty(requestParentService) ? requestParentService : boundParentService;
    if (!string.IsNullOrEmpty(effectiveService) && parentChildren != null)
    {
      CachedParentChildren = (effectiveService, parentId, parentChildren);
    }
  }
}
