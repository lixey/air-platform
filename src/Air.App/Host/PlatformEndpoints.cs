namespace Air.App.Host;

public static class PlatformEndpoints
{
  private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("uk-UA");
  private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

  public static void MapPlatformEndpoints(this WebApplication app)
  {
    var api = app.MapGroup("/api");
    api.MapMetadataEndpoints();
    api.MapAuthEndpoints();
    api.MapFileEndpoints();
    api.MapServiceEndpoints();
  }

  internal static IResult WithRequestCulture(HttpRequest request, Func<CultureInfo, IResult> action)
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
    catch (KeyNotFoundException ex)
    {
      var msg = string.IsNullOrWhiteSpace(ex.Message) ? Localizer.Instance["FormNotFound"] : ex.Message;
      return Results.NotFound(new
      {
        outcome = MutationOutcome.Refused,
        title = msg,
        messages = new[] { new MessageDto { Detail = msg, Code = "refused.notFound", Type = MessageType.Error } }
      });
    }
    finally
    {
      CultureInfo.CurrentCulture = prevCulture;
      CultureInfo.CurrentUICulture = prevUiCulture;
    }
  }

  internal static async Task<IResult> WithRequestCultureAsync(HttpRequest request, Func<CultureInfo, Task<IResult>> action)
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
    catch (KeyNotFoundException ex)
    {
      var msg = string.IsNullOrWhiteSpace(ex.Message) ? Localizer.Instance["FormNotFound"] : ex.Message;
      return Results.NotFound(new
      {
        outcome = MutationOutcome.Refused,
        title = msg,
        messages = new[] { new MessageDto { Detail = msg, Code = "refused.notFound", Type = MessageType.Error } }
      });
    }
    finally
    {
      CultureInfo.CurrentCulture = prevCulture;
      CultureInfo.CurrentUICulture = prevUiCulture;
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

  internal static IResult ToMutationResult(object? result) =>
    result switch
    {
      GridDeleteDto del when del.Outcome != MutationOutcome.Committed =>
        del.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new { outcome = del.Outcome, title = Localizer.Instance["MutationConflict"], messages = del.Warnings })
          : Results.UnprocessableEntity(new { outcome = del.Outcome, title = Localizer.Instance["DeleteRefused"], messages = del.Warnings }),
      GridCellDto cell when cell.Outcome != MutationOutcome.Committed =>
        cell.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new { outcome = cell.Outcome, title = Localizer.Instance["MutationConflict"], messages = cell.Warnings })
          : Results.UnprocessableEntity(new { outcome = cell.Outcome, title = Localizer.Instance["MutationRefused"], messages = cell.Warnings }),
      GridActionDto act when act.Outcome != MutationOutcome.Committed =>
        act.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new { outcome = act.Outcome, title = Localizer.Instance["MutationConflict"], messages = act.Warnings })
          : Results.UnprocessableEntity(new { outcome = act.Outcome, title = Localizer.Instance["MutationRefused"], messages = act.Warnings }),
      FormActionDto fact when fact.Outcome != MutationOutcome.Committed =>
        fact.Outcome == MutationOutcome.Conflict
          ? Results.Conflict(new { outcome = fact.Outcome, title = Localizer.Instance["MutationConflict"], messages = fact.Warnings })
          : Results.UnprocessableEntity(new { outcome = fact.Outcome, title = Localizer.Instance["MutationRefused"], messages = fact.Warnings }),
      _ => Results.Ok(result)
    };
}
