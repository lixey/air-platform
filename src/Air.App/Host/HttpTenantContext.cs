using Air.Data.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Air.App.Host;

/// <summary>
/// Resolves the current tenant for web requests from X-Tenant-Id header, ambient AsyncLocal, or dev default.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
  public long? TenantId
  {
    get
    {
      if (AsyncLocalTenantContext.Instance.TenantId is { } ambient)
      {
        return ambient;
      }

      var httpContext = httpContextAccessor.HttpContext;
      if (httpContext is not null)
      {
        if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerVal) &&
            long.TryParse(headerVal, out var tid))
        {
          return tid;
        }

        // Single tenant fallback for local dev / demo
        return 1;
      }

      return null;
    }
  }
}
