using Platform.Api.Host.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  void IPlatformService.BindContext(PlatformAppService? service) => PlatformApp ??= service;

  [Endpoint(Auth = EndpointAuth.View)]
  public async Task<ScreenDto> GetData(
    FormRequest request,
    HttpRequest httpRequest,
    CancellationToken ct = default)
  {
    var req = request ?? new FormRequest();
    var serviceName = ScreenService.ExtractRouteServicePath(httpRequest) ?? Service;
    var screen = await BuildScreenAsync(serviceName, req, ct);
    var entity = await GetModelAsync(req, ct);
    screen.Data = await BuildFormDataAsync(entity, screen, ct: ct);
    return screen;
  }

  [Endpoint(Auth = EndpointAuth.Custom)]
  public async Task<IResult> Save(
    JsonElement? body,
    HttpRequest httpRequest,
    CancellationToken ct = default)
  {
    if (!body.HasValue)
    {
      return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
    }

    var req = BindFormRequestFromHttp(httpRequest, body.Value);
    var entity = await SaveAsync(body.Value, req, ct);
    return Results.Ok(await BuildFormDataAsync(entity, outcome: MutationOutcome.Committed, ct: ct));
  }

  [Endpoint(Auth = EndpointAuth.View)]
  public async Task<ActionInputDto> GetActionInput(
    FormRequest request,
    JsonElement? body,
    CancellationToken ct = default)
  {
    var actionMethodKey = StatusMoveNavBuilder.ExtractActionMethodKey(body);
    var defaults = await GetActionInputAsync(actionMethodKey, request ?? new FormRequest(), ct);
    return StatusMoveNavBuilder.ToActionInputDto(defaults);
  }

  IEnumerable<string> IPlatformService.GetDynamicEndpointNames(ModuleCatalog catalog) =>
    ((IFormService)this).GetRoutableActionMethods(catalog);

  async Task<IResult?> IPlatformService.ExecuteDynamicEndpointAsync(
    string method,
    HttpRequest httpRequest,
    JsonElement? body,
    IServiceProvider services,
    CancellationToken ct)
  {
    var req = BindFormRequestFromHttp(httpRequest, body ?? default);
    SetRequest(req);

    if (StatusMoveNavBuilder.TryParseMoveStatusEndpoint(method, out var transitionName))
    {
      if (req.Id is null)
      {
        return Results.BadRequest(new { detail = Localizer.Instance["RecordIdRequired"] });
      }

      var moved = await MoveStatusAsync(req.Id.Value, transitionName, body, req.Ver, ct);
      return PlatformEndpointsExtensions.ToMutationResult(moved);
    }

    var result = await ExecuteDeclaredActionAsync(method, req, body, ct);
    return result is not null ? PlatformEndpointsExtensions.ToMutationResult(result) : null;
  }

  private static FormRequest BindFormRequestFromHttp(HttpRequest httpRequest, JsonElement root)
  {
    var (req, _) = FormRequest.Normalize(root);
    ServiceRequestBinder.ApplyScopedHttpMetadata(httpRequest, req, root);
    return req;
  }
}
