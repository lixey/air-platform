using Microsoft.AspNetCore.Authorization;
using Platform.Api.Host.Internals;

namespace Platform.Api.Auth;

[ServiceRoute("platform-language")]
public sealed class PlatformLanguageService(PlatformAppService? app = null) : PlatformService(app)
{
  public override Feature Feature { get; } = new("Platform", "Language", typeof(PlatformLanguageService));

  [AllowAnonymous]
  [Endpoint(RequireClientHeader = true, ResponseType = typeof(UserProfileDto))]
  public async Task<IResult> Switch(
    SwitchRequest req,
    HttpContext httpContext,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct = default)
  {
    var now = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    var resolvedCode = await authStore.ResolveLanguageCodeAsync(req?.Id, ct);
    var targetCulture = !string.IsNullOrWhiteSpace(resolvedCode)
      ? cultures.Resolve(resolvedCode)
      : cultures.ResolveById(req?.Id);
    AuthCookieHelper.AppendCultureCookie(httpContext, targetCulture.ShortCode, now);

    if (userContext.IsAuthenticated && userContext.UserId is { } uid)
    {
      var user = await authStore.FindUserByIdAsync(uid, ct);
      if (user is { IsActive: true })
      {
        if (resolvedCode is not null && req is { Id: > 0 } && user.LanguageId != req.Id)
        {
          user.LanguageId = req.Id;
          await authStore.SaveChangesAsync(ct);
          if (httpContext.RequestServices.GetService<IUserAuthCache>() is { } authCache)
          {
            await authCache.RemoveUserAsync(user.Id, ct);
          }
        }

        var activeTenantId = httpContext.RequestServices.GetService<ITenantContext>()?.TenantId;
        var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(authStore, user, activeTenantId, ct);
        return Results.Ok(await AuthProfileBuilder.BuildUserProfileDtoAsync(
          user,
          securityInfo,
          targetCulture,
          activeTenantId,
          authStore,
          ct));
      }
    }

    return Results.Ok(new UserProfileDto
    {
      Language = targetCulture.ShortCode,
      TimeZone = "UTC",
      Permissions = []
    });
  }
}
