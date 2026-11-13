namespace Platform.Api.Host.Internals;

internal sealed class SafeExceptionMiddleware(RequestDelegate next, ILogger<SafeExceptionMiddleware> logger)
{
  public async Task InvokeAsync(HttpContext context)
  {
    try
    {
      await next(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    catch (Exception ex) when (!context.Response.HasStarted && PlatformEndpointsExtensions.TryMapException(ex, out var mapped))
    {
      await mapped.ExecuteAsync(context);
    }
    catch (Exception ex)
    {
      logger.LogError(
        ex,
        "Unhandled exception on {Method} {Path} (TraceId: {TraceId}).",
        context.Request.Method,
        context.Request.Path,
        context.TraceIdentifier);

      if (context.Response.HasStarted)
      {
        throw;
      }

      context.Response.Clear();
      context.Response.StatusCode = StatusCodes.Status500InternalServerError;
      context.Response.ContentType = "application/json; charset=utf-8";
      await context.Response.WriteAsJsonAsync(
        new
        {
          title = Localizer.Instance["RequestFailed"],
          traceId = context.TraceIdentifier
        },
        PlatformJsonOptions.DefaultOptions,
        CancellationToken.None);
    }
  }
}
