using Platform.Api.Host.Internals;

namespace Platform.Api.Host;

public static class PlatformEndpointsExtensions
{
  private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("uk-UA");
  private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

  public static RouteGroupBuilder MapPlatformApiEndpoints(this WebApplication app)
  {
    var catalog = app.Services.GetRequiredService<ModuleCatalog>();
    ModuleCatalog.Instance = catalog;
    PlatformValidator.Validate(catalog, app.Services);

    var api = app.MapGroup("/api");
    api.MapAuthEndpoints();
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
      case JsonException jsonEx:
        result = Results.BadRequest(new
        {
          detail = string.IsNullOrWhiteSpace(jsonEx.Message) ? "Malformed JSON request body." : jsonEx.Message
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

      default:
        result = Results.Empty;
        return false;
    }
  }

  public static CultureInfo ResolveRequestCulture(HttpRequest req)
  {
    if (req.Query.TryGetValue("lang", out var langVal) && !string.IsNullOrWhiteSpace(langVal))
    {
      var s = langVal.ToString();
      if (s.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
      {
        return UkCulture;
      }

      if (s.StartsWith("en", StringComparison.OrdinalIgnoreCase))
      {
        return EnCulture;
      }
    }

    if (req.Cookies.TryGetValue("culture", out var cookieVal) && !string.IsNullOrWhiteSpace(cookieVal))
    {
      if (cookieVal.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
      {
        return UkCulture;
      }

      if (cookieVal.StartsWith("en", StringComparison.OrdinalIgnoreCase))
      {
        return EnCulture;
      }
    }

    if (req.Headers.TryGetValue("Accept-Language", out var acceptVal) && !string.IsNullOrWhiteSpace(acceptVal))
    {
      var s = acceptVal.ToString();
      if (s.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
      {
        return UkCulture;
      }

      if (s.StartsWith("en", StringComparison.OrdinalIgnoreCase))
      {
        return EnCulture;
      }
    }

    return UkCulture;
  }

  public static IResult ToMutationResult(object? result, string? refusedFallbackKey = null) =>
    result switch
    {
      MutationResultDto mut when mut.Outcome != null && mut.Outcome != MutationOutcome.Committed =>
        mut.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new MutationResultDto
          {
            Outcome = mut.Outcome,
            Title = mut.Title ?? Localizer.Instance["MutationConflict"],
            Messages = mut.Messages ?? mut.Warnings
          })
          : Results.UnprocessableEntity(new MutationResultDto
          {
            Outcome = mut.Outcome,
            Title = mut.Title ?? Localizer.Instance[refusedFallbackKey ?? "MutationRefused"],
            Messages = mut.Messages ?? mut.Warnings
          }),
      _ => Results.Ok(result)
    };
}
