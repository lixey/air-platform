using Platform.Api.Host.Internals;

namespace Platform.App.Host.Internals;

internal static class FormEndpointHandler
{
  public static async Task<IResult> DispatchAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var form = (IFormService)services.GetRequiredService(serviceType);
    feat ??= form.Feature;
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      form.AttachPlatformApp(appInstance);
    }

    var userContext = services.GetService<IUserContext>();
    var lowerMethod = method.ToLowerInvariant();
    if (IsBuiltInFormMethod(lowerMethod, subMethod) && userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    switch (lowerMethod)
    {
      case "get-data":
        {
          if (!ServiceEndpointsExtensions.CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (req, isMalformed) = await ReadFormRequestAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          var screen = await form.BuildScreenAsync(serviceName, req, ct);
          var entity = await form.GetModelAsync(req, ct);
          screen.Data = await form.BuildFormDataAsync(entity, screen, ct: ct);
          return Results.Ok(screen);
        }

      case "save":
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !body.HasValue)
          {
            return Results.BadRequest(new { detail = "Malformed or empty JSON request body." });
          }

          var req = BindFormRequest(httpRequest, body.Value);
          object entity;
          try
          {
            entity = await form.SaveAsync(body.Value, req, ct);
          }
          catch (JsonException)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }
          catch (FormRefusedException ex)
          {
            return MapFormRefused(ex);
          }
          catch (DbUpdateConcurrencyException)
          {
            return Results.Conflict(new MutationResultDto
            {
              Outcome = MutationOutcome.Conflict,
              Title = Localizer.Instance["MutationConflict"],
              Messages = [new MessageDto { Detail = Localizer.Instance["FormRecordChanged"], Type = MessageType.Error }]
            });
          }
          catch (DbUpdateException)
          {
            return Results.UnprocessableEntity(new MutationResultDto
            {
              Outcome = MutationOutcome.Refused,
              Title = Localizer.Instance["MutationRefused"],
              Messages = [new MessageDto { Detail = Localizer.Instance["MutationConstraint"], Type = MessageType.Error }]
            });
          }

          return Results.Ok(await form.BuildFormDataAsync(entity, outcome: MutationOutcome.Committed, ct: ct));
        }

      case "get-action-input":
        {
          if (!ServiceEndpointsExtensions.CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          var actionMethodKey = AppServiceEndpointDispatcher.ResolveActionMethodKey(subMethod, body);
          var req = BindFormRequest(httpRequest, body ?? default);
          var defaults = await form.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(AppServiceEndpointDispatcher.ToActionInputDto(defaults));
        }

      case "move-status" when subMethod != null:
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          var req = BindFormRequest(httpRequest, body ?? default);
          if (req.Id == null)
          {
            return Results.BadRequest(new { detail = "Record id is required for move-status." });
          }

          form.SetRequest(req);
          try
          {
            var result = await form.MoveStatusAsync(req.Id.Value, subMethod, body, req.Ver, ct);
            return PlatformEndpointsExtensions.ToMutationResult(result);
          }
          catch (FormRefusedException ex)
          {
            return MapFormRefused(ex);
          }
        }

      default:
        {
          if (subMethod == null && ServiceRequestBinder.ResolveCallableActionMethod(serviceType, method) == null)
          {
            if (userContext is null or { IsAuthenticated: false })
            {
              return await form.HasActionAsync(method, ct) ? Results.Unauthorized() : Results.NotFound();
            }

            var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
            if (isMalformed)
            {
              return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
            }

            var req = BindFormRequest(httpRequest, body ?? default);
            form.SetRequest(req);
            try
            {
              var result = await form.ExecuteDeclaredActionAsync(method, req, body, ct);
              if (result != null)
              {
                return PlatformEndpointsExtensions.ToMutationResult(result);
              }
            }
            catch (FormRefusedException ex)
            {
              return MapFormRefused(ex);
            }

            return Results.NotFound();
          }

          return await AppServiceEndpointDispatcher.DispatchCustomServiceMethodAsync(serviceType, form, method, feat, httpRequest, services, ct);
        }
    }
  }

  public static FormRequest BindFormRequest(
    HttpRequest httpRequest,
    JsonElement root,
    FormRequest? existing = null)
  {
    var (req, _) = FormRequest.Normalize(root, existing);
    ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      req.IdempotencyKey = idemVals.ToString().Trim();
    }

    if (root.ValueKind != JsonValueKind.Undefined)
    {
      req.RawPayload = root.GetRawText();
    }

    return req;
  }

  private static bool IsBuiltInFormMethod(string lowerMethod, string? subMethod) =>
    lowerMethod is "get-data" or "save" or "get-action-input" ||
    lowerMethod == "move-status" && subMethod != null;

  private static IResult MapFormRefused(FormRefusedException ex)
  {
    var dto = new MutationResultDto
    {
      Outcome = ex.Outcome,
      Title = ex.Title,
      Messages = ex.Messages.ToList()
    };
    return ex.Outcome == MutationOutcome.Conflict ? Results.Conflict(dto) : Results.UnprocessableEntity(dto);
  }

  private static async Task<(FormRequest Request, bool IsMalformed)> ReadFormRequestAsync(HttpRequest httpRequest, CancellationToken ct)
  {
    var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
    return (BindFormRequest(httpRequest, body ?? default), isMalformed);
  }
}
