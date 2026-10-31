namespace Platform.App.Internals;

internal static class ParentScopeValidator
{
  public static async Task<(bool Allowed, object? PreloadedParent, IReadOnlyList<Form.ChildGridBuilder>? ParentChildren)> ValidateAndLoadAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    long? parentId,
    string? requestParentService,
    bool hasRequiredBoundParent,
    Func<string?, bool> matchesService,
    string? boundParentService,
    Type? boundParentServiceType,
    Type? boundParentEntityType,
    bool lockParent,
    CancellationToken ct)
  {
    if (hasRequiredBoundParent && parentId is null or <= 0)
    {
      return (false, null, null);
    }

    if (parentId is not > 0)
    {
      return (true, null, null);
    }

    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && !matchesService(effectiveParentService))
    {
      return (false, null, null);
    }

    var parentServiceType = (!string.IsNullOrEmpty(requestParentService)
      ? ModuleCatalog.Instance.FindServiceType(requestParentService)
      : null) ?? boundParentServiceType;
    var parentEntityType = ModuleCatalog.GetServiceEntityType(parentServiceType) ?? boundParentEntityType;

    if (lockParent && parentEntityType != null)
    {
      await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
    }

    if (parentServiceType != null &&
        typeof(Form.IFormService).IsAssignableFrom(parentServiceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(parentServiceType, db, app) is Form.IFormService parentForm)
    {
      parentForm.SetRequest(new Form.Contracts.FormRequest { Id = parentId });
      var loaded = await parentForm.LoadParentRecordAsync(parentId.Value, ct);
      if (loaded == null)
      {
        return (false, null, null);
      }

      var (_, children) = await parentForm.GetElementsMetadataAsync(ct);
      return (true, loaded, children);
    }

    if (parentEntityType != null)
    {
      var loaded = await db.FindAsync(parentEntityType, [parentId.Value], ct);
      return loaded != null ? (true, loaded, null) : (false, null, null);
    }

    return (true, null, null);
  }
}
