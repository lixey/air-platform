namespace Platform.App.Grid.Internals;

internal sealed class GridExecutionState<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public GridParent<TEntity>? CachedParent { get; set; }
  public GridHooks<TEntity>? CachedHooks { get; set; }
  public object? PreloadedParent { get; set; }
  public long? PreloadedParentId { get; set; }
  public (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? CachedParentChildren { get; set; }
  public (string? ParentService, long? ParentId, GridQuery<TEntity, TRow> Value)? CachedQuery { get; set; }
  public (string? ParentService, long? ParentId, string ServicePath, GridNav Value)? CachedNav { get; set; }
  public Dictionary<string, OverlayDto> CachedFormOverlays { get; } = new(StringComparer.OrdinalIgnoreCase);

  public void CacheParentScope(
    object? preloaded,
    long? parentId,
    string? requestParentService,
    string? boundParentService,
    IReadOnlyList<Form.ChildGridBuilder>? parentChildren)
  {
    PreloadedParent = preloaded;
    PreloadedParentId = parentId;
    var effectiveService = !string.IsNullOrEmpty(requestParentService) ? requestParentService : boundParentService;
    if (!string.IsNullOrEmpty(effectiveService) && parentChildren != null)
    {
      CachedParentChildren = (effectiveService, parentId, parentChildren);
    }
  }
}
