namespace Air.App.Host;

/// <summary>
/// Resolves the current tenant, user, and rights for web requests from Claims, X-Tenant-Id header, or ambient AsyncLocal.
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

        return 1;
      }

      return null;
    }
  }

  public long? UserId
  {
    get
    {
      if (AsyncLocalTenantContext.Instance.UserId is { } ambient)
      {
        return ambient;
      }

      var sub = httpContextAccessor.HttpContext?.User.FindFirst(AuthClaims.Sub)?.Value;
      return long.TryParse(sub, out var uid) ? uid : null;
    }
  }

  public string? CorrelationId =>
    AsyncLocalTenantContext.Instance.CorrelationId ?? httpContextAccessor.HttpContext?.TraceIdentifier;

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
