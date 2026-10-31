namespace Platform.App.Internals;

internal static class ParentScopeValidator
{
  public static async Task<(bool Allowed, object? PreloadedParent)> ValidateAndLoadAsync(
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
      return (false, null);
    }

    if (parentId is not > 0)
    {
      return (true, null);
    }

    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && !matchesService(effectiveParentService))
    {
      return (false, null);
    }

    var parentServiceType = boundParentServiceType
      ?? (!string.IsNullOrEmpty(requestParentService)
        ? ModuleCatalog.Instance.FindServiceType(requestParentService)
        : null);
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
      return loaded != null ? (true, loaded) : (false, null);
    }

    if (parentEntityType != null)
    {
      var loaded = await db.FindAsync(parentEntityType, [parentId.Value], ct);
      return loaded != null ? (true, loaded) : (false, null);
    }

    return (true, null);
  }
}
