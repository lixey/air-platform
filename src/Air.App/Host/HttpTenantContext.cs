using Air.App.Auth;
using Air.Data.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Air.App.Host;

/// <summary>
/// Resolves the current tenant and user rights for web requests from Claims, X-Tenant-Id header, or ambient AsyncLocal.
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
        if (httpContext.User.FindFirst(AuthClaims.Tenant)?.Value is { } tenantClaim && long.TryParse(tenantClaim, out var claimTid))
        {
          return claimTid;
        }

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

  public IReadOnlySet<string>? Rights
  {
    get
    {
      if (AsyncLocalTenantContext.Instance.Rights is { } ambient)
      {
        return ambient;
      }

      var httpContext = httpContextAccessor.HttpContext;
      if (httpContext?.User.Identity?.IsAuthenticated == true)
      {
        return httpContext.User.FindAll(AuthClaims.Permission)
          .Select(c => c.Value)
          .Where(v => !string.IsNullOrWhiteSpace(v))
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
      }

      return null;
    }
  }
}
