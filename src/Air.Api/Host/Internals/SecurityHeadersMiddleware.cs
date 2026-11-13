using Microsoft.Net.Http.Headers;

namespace Platform.Api.Host.Internals;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment? environment = null)
{
  public async Task InvokeAsync(HttpContext context)
  {
    ApplyHeaders(context, environment);
    context.Response.OnStarting(static state =>
    {
      var (ctx, env) = ((HttpContext, IHostEnvironment?))state;
      ApplyHeaders(ctx, env);
      return Task.CompletedTask;
    }, (context, environment));

    await next(context);
  }

  private static void ApplyHeaders(HttpContext ctx, IHostEnvironment? env)
  {
    var headers = ctx.Response.Headers;
    var path = ctx.Request.Path;

    headers.TryAdd(HeaderNames.XContentTypeOptions, "nosniff");
    headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");

    if (!path.StartsWithSegments("/api/platform-files", StringComparison.OrdinalIgnoreCase))
    {
      headers.TryAdd(HeaderNames.XFrameOptions, "DENY");
    }

    if (path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/metadata", StringComparison.OrdinalIgnoreCase))
    {
      headers.TryAdd(HeaderNames.CacheControl, "no-store");
    }

    if (ctx.Request.IsHttps && env?.IsProduction() == true)
    {
      headers.TryAdd(HeaderNames.StrictTransportSecurity, "max-age=31536000; includeSubDomains");
    }
  }
}
