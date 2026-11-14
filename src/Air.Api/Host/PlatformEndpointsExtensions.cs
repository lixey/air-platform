using Microsoft.AspNetCore.Authorization;
using Platform.Api.Host.Internals;

namespace Platform.Api.Host;

public static class PlatformEndpointsExtensions
{
  public static RouteGroupBuilder MapPlatformApiEndpoints(this WebApplication app)
  {
    var catalog = app.Services.GetRequiredService<ModuleCatalog>();
    ModuleCatalog.Instance = catalog;
    PlatformValidator.Validate(catalog, app.Services);

    var api = app.MapGroup("/api");
    api.AddEndpointFilter(static async (ctx, next) =>
      ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
      ctx.HttpContext.User.Identity?.IsAuthenticated == true
        ? await next(ctx)
        : Results.Unauthorized());

    api.MapFileEndpoints();
    api.MapServiceEndpoints();
    return api;
  }

  public static IResult WithRequestCulture(HttpRequest request, Func<CultureInfo, IResult> action)
  {
    var culture = ResolveRequestCulture(request);
    var prevCulture = CultureInfo.CurrentCulture;
    var prevUiCulture = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    try
    {
      return action(culture);
    }
    catch (Exception ex) when (TryMapException(ex, out var mapped))
    {
      return mapped;
    }
    finally
    {
      CultureInfo.CurrentCulture = prevCulture;
      CultureInfo.CurrentUICulture = prevUiCulture;
    }
  }

  public static async Task<IResult> WithRequestCultureAsync(HttpRequest request, Func<CultureInfo, Task<IResult>> action)
  {
    var culture = ResolveRequestCulture(request);
    var prevCulture = CultureInfo.CurrentCulture;
    var prevUiCulture = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    try
    {
      return await action(culture);
    }
    catch (Exception ex) when (TryMapException(ex, out var mapped))
    {
      return mapped;
    }
    finally
    {
      CultureInfo.CurrentCulture = prevCulture;
      CultureInfo.CurrentUICulture = prevUiCulture;
    }
  }

  public static bool TryMapException(Exception ex, out IResult result)
  {
    switch (ex)
    {
      case JsonException:
        result = Results.BadRequest(new
        {
          detail = Localizer.Instance["InvalidJsonRequest"]
        });
        return true;

      case QueryRequestException queryEx:
        result = Results.BadRequest(new
        {
          code = queryEx.Code,
          field = queryEx.Field,
          detail = queryEx.Message
        });
        return true;

      case IMutationRefusedException refusedEx:
        {
          var dto = new MutationResultDto
          {
            Outcome = refusedEx.Outcome,
            Title = refusedEx.Title,
            Messages = [.. refusedEx.Messages]
          };
          result = refusedEx.Outcome == MutationOutcome.Conflict
            ? Results.Conflict(dto)
            : Results.UnprocessableEntity(dto);
          return true;
        }

      case KeyNotFoundException notFoundEx:
        {
          var msg = string.IsNullOrWhiteSpace(notFoundEx.Message) ? Localizer.Instance["FormNotFound"] : notFoundEx.Message;
          result = Results.NotFound(new MutationResultDto
          {
            Outcome = MutationOutcome.Refused,
            Title = msg,
            Messages = [new MessageDto { Detail = msg, Code = "refused.notFound", Type = MessageType.Error }]
          });
          return true;
        }

      case IdempotencyConflictException idemEx:
        result = Results.Conflict(new MutationResultDto
        {
          Outcome = MutationOutcome.Conflict,
          Title = Localizer.Instance["MutationConflict"],
          Messages = [new MessageDto { Detail = idemEx.Message, Code = "conflict.idempotency", Type = MessageType.Error }]
        });
        return true;

      case DbUpdateConcurrencyException:
        result = Results.Conflict(new MutationResultDto
        {
          Outcome = MutationOutcome.Conflict,
          Title = Localizer.Instance["MutationConflict"],
          Messages = [new MessageDto { Detail = Localizer.Instance["FormRecordChanged"], Code = "conflict.concurrency", Type = MessageType.Error }]
        });
        return true;

      case DbUpdateException:
        result = Results.UnprocessableEntity(new MutationResultDto
        {
          Outcome = MutationOutcome.Refused,
          Title = Localizer.Instance["MutationRefused"],
          Messages = [new MessageDto { Detail = Localizer.Instance["MutationConstraint"], Code = "refused.constraint", Type = MessageType.Error }]
        });
        return true;

      default:
        result = Results.Empty;
        return false;
    }
  }

  public static CultureInfo ResolveRequestCulture(HttpRequest req) =>
    ResolvePlatformCulture(req).CultureInfo;

  public static PlatformCulture ResolvePlatformCulture(HttpRequest req) =>
    (req.HttpContext?.RequestServices?.GetService<ICultureCatalog>() ?? CultureCatalog.DefaultInstance)
      .ResolveRequest(req);

  public static IResult ToMutationResult(object? result, string? refusedFallbackKey = null) =>
    result switch
    {
      MutationResultDto mut when mut.Outcome != null && mut.Outcome != MutationOutcome.Committed =>
        mut.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new MutationResultDto
          {
            Outcome = mut.Outcome,
            Title = mut.Title ?? Localizer.Instance["MutationConflict"],
            Messages = mut.Messages
          })
          : Results.UnprocessableEntity(new MutationResultDto
          {
            Outcome = mut.Outcome,
            Title = mut.Title ?? Localizer.Instance[refusedFallbackKey ?? "MutationRefused"],
            Messages = mut.Messages
          }),
      _ => Results.Ok(result)
    };
}
