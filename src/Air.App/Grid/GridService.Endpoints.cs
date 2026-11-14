using Platform.Api.Host.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  void IPlatformService.BindContext(PlatformAppService? service) => PlatformApp ??= service;
  void IPlatformService.ResetExecutionScope() => State.ClearItems();
  bool IPlatformService.HasAsyncDynamicEndpoints =>
    ServiceOverrideFlags.For(GetType()).OverridesSetGridActionsAsync || HasInheritedAsyncFormActions();

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(ScreenDto))]
  public async Task<IResult> GetData(
    GridRequest? request,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct = default)
  {
    var req = request ?? new GridRequest();
    if (!await AuthorizeAndSanitizeReadAsync(req, services, ct))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (req.IsDataOnly)
    {
      return Results.Ok(new ScreenDto { Data = await GetDataAsync(req, ct) });
    }

    var serviceName = ScreenService.ExtractRouteServicePath(httpRequest) ?? ServicePathResolver.Resolve(GetType());
    var screen = await BuildScreenAsync(serviceName, req, ct);
    if (req.Id is not > 0)
    {
      screen.Data = await GetDataAsync(req, ct);
    }

    return Results.Ok(screen);
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(GridCountDto))]
  public async Task<IResult> GetCount(
    GridRequest? request,
    IServiceProvider services,
    CancellationToken ct = default)
  {
    var req = request ?? new GridRequest();
    if (!await AuthorizeAndSanitizeReadAsync(req, services, ct))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(await GetCountAsync(req, ct));
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(RowDto), ResponseNullable = true)]
  public async Task<IResult> GetOne(
    GridRequest request,
    CancellationToken ct = default)
  {
    var req = request ?? new GridRequest();
    if (!CanView(req, User))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (req.Id is not > 0 || !await BindParentAsync(req.ParentService, req.ParentId, ct))
    {
      return Results.NotFound();
    }

    var row = await ReadRowAsync(req.Id.Value, ct);
    return row is null ? Results.NotFound() : Results.Ok(row);
  }

  [Endpoint(Auth = EndpointAuth.Custom)]
  public virtual Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request ?? new GridRequest(), cancellationToken);

  [Endpoint(Auth = EndpointAuth.Edit)]
  public Task<GridCellDto> SaveCell(
    GridCellRequest request,
    IServiceProvider services,
    CancellationToken ct = default) =>
    SaveCellAsync(request ?? new GridCellRequest(), services, ct);

  [Endpoint(Auth = EndpointAuth.View, RequestType = typeof(GridActionInputRequest))]
  public async Task<ActionInputDto> GetActionInput(
    GridRequest request,
    JsonElement? body,
    CancellationToken ct = default)
  {
    var actionMethodKey = StatusMoveNavBuilder.ExtractActionMethodKey(body);
    var defaults = await GetActionInputAsync(actionMethodKey, request ?? new GridRequest(), ct);
    return StatusMoveNavBuilder.ToActionInputDto(defaults);
  }

  [Endpoint(Auth = EndpointAuth.Export, ResponseType = typeof(Stream))]
  public Task<IResult> Export(GridRequest? request, CancellationToken ct = default) =>
    ExportAsync(request ?? new GridRequest(), ct);

  IEnumerable<string> IPlatformService.GetDynamicEndpointNames(ModuleCatalog catalog) =>
    ((IGridService)this).GetRoutableActionMethods(catalog);

  async Task<IResult?> IPlatformService.ExecuteDynamicEndpointAsync(
    string method,
    HttpRequest httpRequest,
    JsonElement? body,
    IServiceProvider services,
    CancellationToken ct)
  {
    var req = body.HasValue
      ? JsonSerializer.Deserialize<GridRequest>(body.Value.GetRawText(), PlatformJsonOptions.DefaultOptions) ?? new GridRequest()
      : new GridRequest();
    ServiceRequestBinder.ApplyScopedHttpMetadata(httpRequest, req, body);

    if (StatusMoveNavBuilder.TryParseMoveStatusEndpoint(method, out var transitionName))
    {
      return PlatformEndpointsExtensions.ToMutationResult(await MoveStatusAsync(req, transitionName, body, ct));
    }

    var res = await ExecuteDeclaredActionAsync(method, req, body, ct);
    return res is not null ? PlatformEndpointsExtensions.ToMutationResult(res) : null;
  }

  private async Task<bool> AuthorizeAndSanitizeReadAsync(
    GridRequest req,
    IServiceProvider services,
    CancellationToken ct)
  {
    var isPicker = req.IsReferencePicker || req.ReferenceLabelPaths is { Count: > 0 };
    if (isPicker)
    {
      await ReferenceLookupAllowlist.EnsureWarmAsync(services, ct);
    }

    var canView = CanView(req, User);
    if (!canView && (!isPicker || !CanLookup(req, User)))
    {
      return false;
    }

    if (!canView)
    {
      var serviceType = GetType();
      if (!ReferenceLookupAllowlist.TrySanitizeFilter(serviceType, req.Filter, out var sanitizedFilter))
      {
        return false;
      }

      req.IsReferencePicker = true;
      req.IsDataOnly = true;
      req.Filter = sanitizedFilter;
      req.ColumnFilter = null;
      req.NamedFilters = null;
      req.PatchSourceColumns = ReferenceLookupAllowlist.FilterPatchColumns(serviceType, req.PatchSourceColumns);
      req.ReferenceLabelPaths = ReferenceLookupAllowlist.FilterLabelPaths(serviceType, GetReferenceLabelPaths(), req.ReferenceLabelPaths);
      req.PageSize = Math.Min(req.PageSize > 0 ? req.PageSize : 50, 50);
    }

    return true;
  }
}
