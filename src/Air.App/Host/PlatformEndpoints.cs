using System.Text.Json;
using Air.App.Auth;
using Air.App.Grid;
using Air.App.Form;
using Air.App.Localization;
using Air.App.Metadata;
using Air.App.Modules;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Host;

public static class PlatformEndpoints
{
  public static void MapPlatformEndpoints(this WebApplication app)
  {
    var api = app.MapGroup("/api");

    // 1. Metadata (both GET and POST for convenience)
    var getMetadataHandler = async (ModuleCatalog catalog, HttpContext httpContext, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      var culture = ResolveRequestCulture(httpContext.Request);
      var prevCulture = System.Globalization.CultureInfo.CurrentCulture;
      var prevUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
      System.Globalization.CultureInfo.CurrentCulture = culture;
      System.Globalization.CultureInfo.CurrentUICulture = culture;

      try
      {
        Localizer.Instance.EnsureManagersLoaded();
        var texts = UiTexts.English.Keys.ToDictionary(k => k, k => Localizer.Instance[k]);
        var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

        var tenants = await authStore.GetActiveTenantsAsync(ct);
        var activeTenantId = AsyncLocalTenantContext.Instance.TenantId ?? (userContext.IsAuthenticated && userContext.Claim(AuthClaims.Tenant) is { } tc && long.TryParse(tc, out var tId) ? tId : 1);
        var currentTenant = tenants.FirstOrDefault(t => t.Id == activeTenantId) ?? tenants.FirstOrDefault();

        var metadata = new AppMetadataDto
        {
          Texts = texts,
          Language = new SwitcherDto
          {
            Label = Localizer.Instance["Language"],
            Current = isUk ? new Reference { Id = 1, Name = Localizer.Instance["Ukrainian"] } : new Reference { Id = 2, Name = Localizer.Instance["English"] },
            Options = [
              new Reference { Id = 1, Name = Localizer.Instance["Ukrainian"] },
              new Reference { Id = 2, Name = Localizer.Instance["English"] }
            ],
            Service = "platform-language",
            Method = "switch"
          },
          Tenant = currentTenant != null ? new SwitcherDto
          {
            Label = Localizer.Instance["Tenant"],
            Current = new Reference { Id = currentTenant.Id, Name = currentTenant.Name },
            Options = tenants.Select(t => new Reference { Id = t.Id, Name = t.Name }).ToList(),
            Service = "platform-tenant",
            Method = "switch"
          } : null,
          DefaultCurrency = "UAH"
        };

        foreach (var module in catalog.Modules)
        {
          var visibleNodes = module.Menu.Nodes
            .Select(n => ToMenuItem(module.Handle, n, userContext))
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();

          var trimmedNodes = TrimSeparators(visibleNodes);
          if (trimmedNodes.Count == 0) continue;

          var rootItem = new MenuItemDto
          {
            Handle = module.Handle,
            Name = Localizer.Instance[module.Name],
            Path = module.Handle.ToLowerInvariant(),
            Items = trimmedNodes
          };
          metadata.Menu.Add(rootItem);
          metadata.Routes.AddRange(Flatten(rootItem));
        }

        foreach (var feat in catalog.AllFeatures)
        {
          if (userContext.IsAuthenticated && !userContext.Can(feat.View))
          {
            continue;
          }

          var screen = feat.ServiceType != null ? ServicePathResolver.Resolve(feat.ServiceType) : feat.Path;
          if (metadata.Routes.All(r => r.Screen != screen && r.Path != feat.Path))
          {
            metadata.Routes.Add(new MenuItemDto
            {
              Path = feat.Path,
              Handle = feat.Name.ToLowerInvariant(),
              Name = Localizer.Instance[feat.Name],
              Screen = screen
            });
          }

          var kebabPath = ServicePathResolver.ToKebabCase(feat.Path);
          if (!string.Equals(kebabPath, feat.Path, StringComparison.OrdinalIgnoreCase) && metadata.Routes.All(r => r.Path != kebabPath))
          {
            metadata.Routes.Add(new MenuItemDto
            {
              Path = kebabPath,
              Handle = feat.Name.ToLowerInvariant(),
              Name = Localizer.Instance[feat.Name],
              Screen = screen
            });
          }
        }

        return Results.Ok(metadata);
      }
      finally
      {
        System.Globalization.CultureInfo.CurrentCulture = prevCulture;
        System.Globalization.CultureInfo.CurrentUICulture = prevUiCulture;
      }
    };

    api.MapGet("/metadata", getMetadataHandler);
    api.MapPost("/metadata/get-data", getMetadataHandler);

    // 2. Auth Endpoints
    api.MapPost("/auth/get-login-screen", (HttpContext ctx) =>
    {
      var culture = ResolveRequestCulture(ctx.Request);
      return Results.Ok(new LoginScreenDto
      {
        Title = Localizer.Instance["SignIn"],
        Login = Localizer.Instance["Login"],
        Password = Localizer.Instance["Password"],
        Submit = Localizer.Instance["SignIn"],
        InvalidCredentials = Localizer.Instance["InvalidCredentials"],
        TooManyAttempts = Localizer.Instance["TooManyAttempts"],
        Unavailable = Localizer.Instance["Unavailable"],
        ChangePasswordTitle = Localizer.Instance["ChangePasswordTitle"],
        CurrentPassword = Localizer.Instance["PasswordCurrent"],
        NewPassword = Localizer.Instance["PasswordNew"],
        RepeatPassword = Localizer.Instance["PasswordRepeat"],
        ChangePasswordSubmit = Localizer.Instance["ChangePasswordSubmit"],
        PasswordsDiffer = Localizer.Instance["PasswordsDiffer"],
        SignOut = Localizer.Instance["SignOut"],
        Culture = culture.Name
      });
    });

    api.MapPost("/auth/get-profile", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (!userContext.IsAuthenticated || userContext.UserId is null)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var culture = ResolveRequestCulture(ctx.Request);
      var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);
      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);

      var profile = new UserProfileDto
      {
        Id = user.Id,
        Login = user.Login,
        Name = user.Name ?? user.Login,
        Email = user.Email ?? $"{user.Login}@demo.invalid",
        Language = isUk ? "uk" : "en",
        TimeZone = user.TimeZone ?? "UTC",
        MustChangePassword = user.MustChangePassword,
        Permissions = securityInfo.Permissions.ToList()
      };

      return Results.Ok(profile);
    });

    api.MapPost("/auth/login", async (LoginRequest? req, HttpContext ctx, IAuthStore authStore, IPasswordHasher hasher, CancellationToken ct) =>
    {
      var culture = ResolveRequestCulture(ctx.Request);
      var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);
      var login = req?.Login?.Trim() ?? "";

      if (string.IsNullOrWhiteSpace(login))
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByLoginAsync(login, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      if (!string.IsNullOrEmpty(user.PasswordHash))
      {
        if (string.IsNullOrEmpty(req?.Password) || hasher.VerifyPassword(req.Password, user.PasswordHash) == PasswordVerification.Failed)
        {
          return Results.Unauthorized();
        }
      }

      var tenantId = (user as IHasTenant)?.TenantId;
      var session = authStore.CreateSession(
        user.Id,
        tenantId,
        Guid.NewGuid(),
        SessionTokenService.HashRefreshToken(SessionTokenService.CreateRefreshToken()),
        DateTimeOffset.UtcNow.AddDays(30),
        ctx.Request.Headers.UserAgent.ToString(),
        ctx.Connection.RemoteIpAddress?.ToString()
      );
      await authStore.SaveChangesAsync(ct);

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30));
      var refreshToken = SessionTokenService.CreateRefreshToken();

      ctx.Response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddMinutes(30),
        SameSite = SameSiteMode.Lax,
        HttpOnly = false
      });

      ctx.Response.Cookies.Append(AuthClaims.RefreshCookie, refreshToken, new CookieOptions
      {
        Path = "/api/auth",
        Expires = DateTimeOffset.UtcNow.AddDays(30),
        SameSite = SameSiteMode.Lax,
        HttpOnly = true
      });

      var sessionDto = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = user.Id,
          Login = user.Login,
          Name = user.Name ?? user.Login,
          Email = user.Email ?? $"{user.Login}@demo.invalid",
          Language = isUk ? "uk" : "en",
          TimeZone = user.TimeZone ?? "UTC",
          MustChangePassword = user.MustChangePassword,
          Permissions = securityInfo.Permissions.ToList()
        },
        ExpiresIn = 1800
      };

      return Results.Ok(sessionDto);
    });

    api.MapPost("/auth/change-password", async (ChangePasswordRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, IPasswordHasher hasher, CancellationToken ct) =>
    {
      if (!userContext.IsAuthenticated || userContext.UserId is null)
      {
        return Results.Unauthorized();
      }

      if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
      {
        return Results.BadRequest(new { error = "New password is required." });
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      if (!string.IsNullOrEmpty(user.PasswordHash))
      {
        if (string.IsNullOrEmpty(req.CurrentPassword) || hasher.VerifyPassword(req.CurrentPassword, user.PasswordHash) == PasswordVerification.Failed)
        {
          return Results.BadRequest(new { error = "Current password is incorrect." });
        }
      }

      user.PasswordHash = hasher.HashPassword(req.NewPassword);
      user.MustChangePassword = false;
      user.TokenVersion++;
      await authStore.SaveChangesAsync(ct);

      long? sessionId = userContext.SessionId;
      var session = sessionId != null ? await authStore.FindSessionAsync(sessionId.Value, ct) : null;
      if (session != null)
      {
        session.ExpiresAt = DateTimeOffset.UtcNow.AddDays(30);
        session.LastUsedAt = DateTimeOffset.UtcNow;
        await authStore.SaveChangesAsync(ct);
      }
      else
      {
        session = authStore.CreateSession(
          user.Id,
          (user as IHasTenant)?.TenantId,
          Guid.NewGuid(),
          SessionTokenService.HashRefreshToken(SessionTokenService.CreateRefreshToken()),
          DateTimeOffset.UtcNow.AddDays(30),
          ctx.Request.Headers.UserAgent.ToString(),
          ctx.Connection.RemoteIpAddress?.ToString()
        );
        await authStore.SaveChangesAsync(ct);
      }

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var culture = ResolveRequestCulture(ctx.Request);
      var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

      var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30));
      ctx.Response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddMinutes(30),
        SameSite = SameSiteMode.Lax,
        HttpOnly = false
      });

      var sessionDto = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = user.Id,
          Login = user.Login,
          Name = user.Name ?? user.Login,
          Email = user.Email ?? $"{user.Login}@demo.invalid",
          Language = isUk ? "uk" : "en",
          TimeZone = user.TimeZone ?? "UTC",
          MustChangePassword = false,
          Permissions = securityInfo.Permissions.ToList()
        },
        ExpiresIn = 1800
      };

      return Results.Ok(sessionDto);
    });

    api.MapPost("/auth/refresh", async (RefreshRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      long? sessionId = userContext.SessionId;
      if (sessionId == null && ctx.Request.Cookies.TryGetValue(AuthClaims.AccessCookie, out var cookieToken) &&
          SessionTokenService.TryValidateAccessToken(cookieToken, out var sId, out _, out _))
      {
        sessionId = sId;
      }

      if (sessionId == null)
      {
        return Results.Unauthorized();
      }

      var session = await authStore.FindSessionAsync(sessionId.Value, ct);
      if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(session.UserId, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var culture = ResolveRequestCulture(ctx.Request);
      var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

      var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30));
      ctx.Response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddMinutes(30),
        SameSite = SameSiteMode.Lax,
        HttpOnly = false
      });

      var sessionDto = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = user.Id,
          Login = user.Login,
          Name = user.Name ?? user.Login,
          Email = user.Email ?? $"{user.Login}@demo.invalid",
          Language = isUk ? "uk" : "en",
          TimeZone = user.TimeZone ?? "UTC",
          MustChangePassword = user.MustChangePassword,
          Permissions = securityInfo.Permissions.ToList()
        },
        ExpiresIn = 1800
      };

      return Results.Ok(sessionDto);
    });

    api.MapPost("/auth/logout", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (userContext.SessionId is { } sid)
      {
        var session = await authStore.FindSessionAsync(sid, ct);
        if (session != null)
        {
          session.RevokedAt = DateTimeOffset.UtcNow;
          session.RevokedReason = "self";
          await authStore.SaveChangesAsync(ct);
        }
      }

      ctx.Response.Cookies.Delete(AuthClaims.AccessCookie, new CookieOptions { Path = "/" });
      ctx.Response.Cookies.Delete(AuthClaims.RefreshCookie, new CookieOptions { Path = "/api/auth" });

      return Results.Ok(new { });
    });

    var switchLanguageHandler = async (SwitchRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      var lang = req?.Id == 1 ? "uk" : "en";
      ctx.Response.Cookies.Append("culture", lang, new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddYears(1),
        SameSite = SameSiteMode.Lax
      });

      if (userContext.IsAuthenticated && userContext.UserId is { } uid)
      {
        var user = await authStore.FindUserByIdAsync(uid, ct);
        if (user != null && user.IsActive)
        {
          var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
          return Results.Ok(new UserProfileDto
          {
            Id = user.Id,
            Login = user.Login,
            Name = user.Name ?? user.Login,
            Email = user.Email ?? $"{user.Login}@demo.invalid",
            Language = lang,
            TimeZone = user.TimeZone ?? "UTC",
            MustChangePassword = user.MustChangePassword,
            Permissions = securityInfo.Permissions.ToList()
          });
        }
      }

      var defaultUser = new UserProfileDto
      {
        Id = 1,
        Login = "admin",
        Name = "Administrator",
        Email = "admin@air.platform",
        Language = lang,
        TimeZone = "UTC",
        Permissions = ["*"]
      };
      return Results.Ok(defaultUser);
    };

    api.MapPost("/platform-language/switch", switchLanguageHandler);
    api.MapPost("/PlatformLanguageService/Switch", switchLanguageHandler);

    var switchTenantHandler = async (SwitchRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (req == null)
      {
        return Results.BadRequest(new { error = "Tenant id is required." });
      }

      if (!userContext.IsAuthenticated || userContext.UserId is null)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      long? sessionId = userContext.SessionId;
      if (sessionId == null && ctx.Request.Cookies.TryGetValue(AuthClaims.AccessCookie, out var cookieToken) &&
          SessionTokenService.TryValidateAccessToken(cookieToken, out var sId, out _, out _))
      {
        sessionId = sId;
      }

      if (sessionId == null)
      {
        return Results.Unauthorized();
      }

      var session = await authStore.FindSessionAsync(sessionId.Value, ct);
      if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
      {
        return Results.Unauthorized();
      }

      var targetTenant = await authStore.FindTenantByIdAsync(req.Id, ct);
      if (targetTenant == null || !targetTenant.IsActive)
      {
        return Results.BadRequest(new { error = "Select an active tenant." });
      }

      session.TenantId = req.Id;
      session.LastUsedAt = DateTimeOffset.UtcNow;
      await authStore.SaveChangesAsync(ct);

      SecurityInfo securityInfo;
      using (AsyncLocalTenantContext.Use(req.Id, null))
      {
        securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      }

      var culture = ResolveRequestCulture(ctx.Request);
      var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

      var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30));
      ctx.Response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddMinutes(30),
        SameSite = SameSiteMode.Lax,
        HttpOnly = false
      });

      var sessionDto = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = user.Id,
          Login = user.Login,
          Name = user.Name ?? user.Login,
          Email = user.Email ?? $"{user.Login}@demo.invalid",
          Language = isUk ? "uk" : "en",
          TimeZone = user.TimeZone ?? "UTC",
          MustChangePassword = user.MustChangePassword,
          Permissions = securityInfo.Permissions.ToList()
        },
        ExpiresIn = 1800
      };

      return Results.Ok(sessionDto);
    };

    api.MapPost("/platform-tenant/switch", switchTenantHandler);
    api.MapPost("/PlatformTenantService/Switch", switchTenantHandler);

    // 2.1 Signed Platform Files Endpoint
    api.MapGet("/platform-files/{token}", async (string token, HttpContext httpContext) =>
    {
      var links = new Files.FileLinks();
      var link = links.Read(token);
      if (link == null)
      {
        return Results.NotFound();
      }

      var storage = new Data.Files.LocalFileStorage();
      var stream = await storage.OpenReadAsync(link.Key, httpContext.RequestAborted);
      if (stream == null)
      {
        return Results.NotFound();
      }

      var type = link.ContentType;
      var name = link.Name;

      if (link.IsThumbnail)
      {
        var head = new byte[Files.Internal.FileTypeTable.ThumbnailHeadBytes];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, httpContext.RequestAborted);
        if (Files.Internal.FileTypeTable.ThumbnailTypeOf(head.AsSpan(0, read)) is not { } thumbnail)
        {
          return Results.NotFound();
        }
        stream.Seek(0, SeekOrigin.Begin);
        type = thumbnail;
        name = Path.GetFileNameWithoutExtension(link.Name) + (thumbnail == Air.Data.Entities.FileType.WebP ? ".webp" : ".jpg");
      }

      var isInline = !httpContext.Request.Query.ContainsKey("download") &&
                     (link.IsThumbnail || Files.Internal.FileTypeTable.IsServedInline(type));

      var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(isInline ? "inline" : "attachment");
      disposition.SetHttpFileName(name);
      httpContext.Response.Headers.ContentDisposition = disposition.ToString();
      httpContext.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.XContentTypeOptions] = "nosniff";

      return Results.Stream(stream, type, enableRangeProcessing: true);
    });

    // 3. Dynamic service endpoints
    api.MapPost("/{**servicePath}", async (
      string servicePath,
      HttpRequest httpRequest,
      ModuleCatalog catalog,
      IServiceProvider services,
      CancellationToken ct) =>
    {
      var culture = ResolveRequestCulture(httpRequest);
      var prevCulture = System.Globalization.CultureInfo.CurrentCulture;
      var prevUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
      System.Globalization.CultureInfo.CurrentCulture = culture;
      System.Globalization.CultureInfo.CurrentUICulture = culture;

      try
      {
        Localizer.Instance.EnsureManagersLoaded();
        var parts = servicePath.Trim('/').Split('/');
        if (parts.Length < 2)
        {
          return Results.NotFound();
        }

      string serviceName;
      string method;
      string? subMethod = null;

      if (parts.Length >= 3 && string.Equals(parts[^2], "move-status", StringComparison.OrdinalIgnoreCase))
      {
        serviceName = string.Join('/', parts[..^2]);
        method = "move-status";
        subMethod = parts[^1];
      }
      else
      {
        serviceName = string.Join('/', parts[..^1]);
        method = parts[^1];
      }

      // Find feature by path or service name
      var feat = catalog.FindFeature(serviceName)
        ?? catalog.FindFeature(serviceName.Replace('/', '-'))
        ?? catalog.FindFeature(serviceName.Replace('-', '/'))
        ?? catalog.FindFeature(serviceName.Replace("demo/", ""))
        ?? catalog.FindFeature(serviceName.Replace("-", ""))
        ?? catalog.FindFeature($"demo/{serviceName}")
        ?? (parts.Length >= 2 ? catalog.FindFeature(parts[0] + "/" + parts[1]) : null);

      var serviceType = feat?.ServiceType
        ?? catalog.FindServiceType(serviceName)
        ?? catalog.FindServiceType(serviceName.Replace('/', '-'))
        ?? catalog.FindServiceType(serviceName.Replace('-', '/'))
        ?? catalog.FindServiceType(serviceName.Replace("demo/", ""))
        ?? catalog.FindServiceType(serviceName.Replace("-", ""))
        ?? catalog.FindServiceType($"demo/{serviceName}")
        ?? (parts.Length >= 2 ? catalog.FindServiceType(parts[0] + "/" + parts[1]) : null);

      if (serviceType is null)
      {
        return Results.NotFound($"Service '{serviceName}' not found.");
      }

      if (typeof(IGridService).IsAssignableFrom(serviceType))
      {
        var grid = (IGridService)services.GetRequiredService(serviceType);
        feat ??= grid.Feature;
        if (services.GetService<PlatformAppService>() is { } appInstance)
        {
          grid.AttachPlatformApp(appInstance);
        }

        if (method == "get-data")
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var screen = await grid.BuildScreenAsync(serviceName, req, ct);
          var page = await grid.GetDataAsync(req, ct);
          screen.Data = page;
          return Results.Ok(screen);
        }

        if (method == "get-count")
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var countDto = await grid.GetCountAsync(req, ct);
          return Results.Ok(countDto);
        }

        if (method == "delete")
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal))
          {
            req.Id = idVal;
          }
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal))
          {
            req.ParentId = pidVal;
          }
          if (httpRequest.Query.TryGetValue("parentService", out var psStr))
          {
            req.ParentService = psStr;
          }

          var result = await grid.DeleteAsync(req, ct);
          return Results.Ok(result);
        }

        if (method == "save-cell")
        {
          GridCellRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridCellRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridCellRequest();
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal)) req.Id = idVal;
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var result = await grid.SaveCellAsync(req, services, ct);
          return Results.Ok(result);
        }

        if (method == "move-status")
        {
          GridRequest? req = null;
          JsonElement? bodyElement = null;
          if (httpRequest.ContentLength > 0)
          {
            try
            {
              using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              bodyElement = doc.RootElement.Clone();
              req = JsonSerializer.Deserialize<GridRequest>(bodyElement.Value.GetRawText(), PlatformJsonOptions.DefaultOptions);
            }
            catch { }
          }
          req ??= new GridRequest();
          var actionResult = await grid.MoveStatusAsync(req, subMethod ?? "", bodyElement, ct);
          return Results.Ok(actionResult);
        }

        if (string.Equals(method, "export", StringComparison.OrdinalIgnoreCase))
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal))
          {
            req.ParentId = pidVal;
          }
          return await grid.ExportAsync(req, ct);
        }

        if (string.Equals(method, "upload", StringComparison.OrdinalIgnoreCase))
        {
          var uploadMethod = serviceType.GetMethod("Upload");
          if (uploadMethod != null && httpRequest.HasFormContentType)
          {
            var formCollection = await httpRequest.ReadFormAsync(ct);
            var uploadReq = new Files.Contracts.GridUploadRequest();
            if (formCollection.TryGetValue("parentService", out var psVal)) uploadReq.ParentService = psVal;
            if (formCollection.TryGetValue("parentId", out var pidVal) && long.TryParse(pidVal, out var parsedPid)) uploadReq.ParentId = parsedPid;
            if (formCollection.TryGetValue("afterId", out var aidVal) && long.TryParse(aidVal, out var parsedAid)) uploadReq.AfterId = parsedAid;
            if (formCollection.TryGetValue("metadata", out var metaVal)) uploadReq.Metadata = metaVal;

            if (httpRequest.Query.TryGetValue("parentId", out var qPid) && long.TryParse(qPid, out var parsedQPid)) uploadReq.ParentId = parsedQPid;
            if (httpRequest.Query.TryGetValue("parentService", out var qPs)) uploadReq.ParentService = qPs;

            var taskObj = uploadMethod.Invoke(grid, [uploadReq, formCollection.Files, ct]);
            if (taskObj is Task<GridActionDto> typedTask)
            {
              return Results.Ok(await typedTask);
            }
            if (taskObj is Task task)
            {
              await task.ConfigureAwait(false);
              var resProp = task.GetType().GetProperty("Result");
              return Results.Ok(resProp?.GetValue(task));
            }
          }
        }

        // Custom action dispatching on grid
        var actionMethod = serviceType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
          .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(m.Name, method.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
        if (actionMethod != null)
        {
          var reqAttr = actionMethod.GetCustomAttribute<Security.RequireRightAttribute>();
          var userContext = services.GetService<IUserContext>();
          if (userContext != null && userContext.IsAuthenticated)
          {
            bool permitted = true;
            if (reqAttr != null)
            {
              if (Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat))
              {
                permitted = userContext.Can(targetFeat.Action(reqAttr.Action.ToString())) || userContext.Can(targetFeat.All) || userContext.Can("*");
              }
            }
            else
            {
              var targetKey = feat?.Edit?.Key;
              permitted = targetKey != null ? (userContext.Can(targetKey) || userContext.Can(feat!.All) || userContext.Can("*")) : userContext.Can("*");
            }

            if (!permitted)
            {
              return Results.Ok(new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Warnings = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
              });
            }
          }

          var parameters = actionMethod.GetParameters();
          var pickParam = parameters.FirstOrDefault(p => typeof(GridRequest).IsAssignableFrom(p.ParameterType));
          var pickReqType = pickParam?.ParameterType ?? typeof(GridRequest);

          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = (GridRequest?)await httpRequest.ReadFromJsonAsync(pickReqType, cancellationToken: ct); } catch { }
          }
          req ??= (GridRequest)Activator.CreateInstance(pickReqType)!;
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal)) req.Id = idVal;
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          object?[] args = new object?[parameters.Length];
          for (int i = 0; i < parameters.Length; i++)
          {
            var pType = parameters[i].ParameterType;
            if (pType == typeof(CancellationToken)) args[i] = ct;
            else if (typeof(GridRequest).IsAssignableFrom(pType)) args[i] = req;
            else if (pType == typeof(long) && req.Id.HasValue) args[i] = req.Id.Value;
            else args[i] = null;
          }

          var resultTask = actionMethod.Invoke(grid, args);
          if (resultTask is Task<IResult> taskResult)
          {
            return await taskResult;
          }
          if (resultTask is Task task)
          {
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result");
            var result = resultProp?.GetValue(task);
            if (result is IResult r) return r;
            return Results.Ok(result ?? new GridActionDto { Outcome = MutationOutcome.Committed });
          }
          if (resultTask is IResult immediateResult) return immediateResult;
          return Results.Ok(resultTask ?? new GridActionDto { Outcome = MutationOutcome.Committed });
        }
      }
      else if (typeof(IFormService).IsAssignableFrom(serviceType))
      {
        var form = (IFormService)services.GetRequiredService(serviceType);
        if (services.GetService<PlatformAppService>() is { } appInstance)
        {
          form.AttachPlatformApp(appInstance);
        }

        if (method == "get-data")
        {
          var req = new FormRequest();
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
          {
            req.Id = parsedId;
          }
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var parsedPid))
          {
            req.ParentId = parsedPid;
          }
          if (httpRequest.Query.TryGetValue("parentService", out var psStr))
          {
            req.ParentService = psStr;
          }

          if (httpRequest.ContentLength > 0)
          {
            try
            {
              using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var bodyId))
              {
                req.Id = bodyId;
              }
              if (doc.RootElement.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var bodyPid))
              {
                req.ParentId = bodyPid;
              }
              if (doc.RootElement.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } bodyPs)
              {
                req.ParentService = bodyPs;
              }
            }
            catch { }
          }

          var screen = await form.BuildScreenAsync(serviceName, req, ct);
          var entity = await form.GetModelAsync(req, ct);
          var entityId = (entity as Air.Data.Entities.IEntity)?.Id ?? req.Id;
          var ver = (entity as Air.Data.Entities.IHasVersion)?.Ver ?? 1;

          var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
          var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
          await PopulateFormReferencesAsync(dict, entity, screen, form.DbContext, catalog, ct);

          screen.Data = new FormDataDto
          {
            Id = (entityId > 0) ? entityId : null,
            Ver = (entityId > 0) ? ver : null,
            Value = dict
          };

          return Results.Ok(screen);
        }

        if (method == "save")
        {
          using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
          var req = new FormRequest();
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
          {
            req.Id = parsedId;
          }
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var parsedPid))
          {
            req.ParentId = parsedPid;
          }
          if (httpRequest.Query.TryGetValue("parentService", out var psStr))
          {
            req.ParentService = psStr;
          }

          if (doc.RootElement.ValueKind == JsonValueKind.Object)
          {
            if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var bodyId))
            {
              req.Id = bodyId;
            }
            if (doc.RootElement.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var bodyPid))
            {
              req.ParentId = bodyPid;
            }
            if (doc.RootElement.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } bodyPs)
            {
              req.ParentService = bodyPs;
            }
          }

          var entity = await form.SaveAsync(doc.RootElement, req, ct);
          var entityId = (entity as Air.Data.Entities.IEntity)?.Id;
          var ver = (entity as Air.Data.Entities.IHasVersion)?.Ver;

          var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
          var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
          req.Id = entityId;
          var screen = await form.BuildScreenAsync(serviceName, req, ct);
          await PopulateFormReferencesAsync(dict, entity, screen, form.DbContext, catalog, ct);

          return Results.Ok(new FormDataDto
          {
            Id = entityId,
            Ver = ver,
            Value = dict,
            Outcome = MutationOutcome.Committed
          });
        }

        if (method == "move-status" && subMethod != null)
        {
          long? id = null;
          JsonElement? bodyElement = null;
          JsonDocument? jsonDoc = null;
          if (httpRequest.ContentLength > 0)
          {
            try
            {
              jsonDoc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              bodyElement = jsonDoc.RootElement;
              if (bodyElement.Value.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var parsedId))
              {
                id = parsedId;
              }
            }
            catch { }
          }

          if (id == null && httpRequest.Query.TryGetValue("id", out var qIdStr) && long.TryParse(qIdStr, out var qId))
          {
            id = qId;
          }

          if (id == null)
          {
            return Results.BadRequest(new { error = "Record id is required for move-status." });
          }

          var result = await form.MoveStatusAsync(id.Value, subMethod, bodyElement, ct);
          jsonDoc?.Dispose();

          if (result.Outcome == "refused")
          {
            return Results.UnprocessableEntity(new { messages = result.Warnings });
          }

          return Results.Ok(result);
        }

        if (method == "delete")
        {
          var ids = await ExtractIdsAsync(httpRequest, ct);
          int deleted = 0;
          foreach (var id in ids)
          {
            await form.DeleteAsync(id, ct);
            deleted++;
          }
          return Results.Ok(new GridDeleteDto { Deleted = deleted, Outcome = MutationOutcome.Committed });
        }
      }

      return Results.NotFound();
      }
      finally
      {
        System.Globalization.CultureInfo.CurrentCulture = prevCulture;
        System.Globalization.CultureInfo.CurrentUICulture = prevUiCulture;
      }
    });
  }

  private static System.Globalization.CultureInfo ResolveRequestCulture(HttpRequest req)
  {
    if (req.Query.TryGetValue("lang", out var langVal) && !string.IsNullOrWhiteSpace(langVal))
    {
      var s = langVal.ToString();
      if (s.StartsWith("uk", StringComparison.OrdinalIgnoreCase)) return new System.Globalization.CultureInfo("uk-UA");
      if (s.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return System.Globalization.CultureInfo.InvariantCulture;
    }

    if (req.Cookies.TryGetValue("culture", out var cookieVal) && !string.IsNullOrWhiteSpace(cookieVal))
    {
      if (cookieVal.StartsWith("uk", StringComparison.OrdinalIgnoreCase)) return new System.Globalization.CultureInfo("uk-UA");
      if (cookieVal.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return System.Globalization.CultureInfo.InvariantCulture;
    }

    if (req.Headers.TryGetValue("Accept-Language", out var acceptVal) && !string.IsNullOrWhiteSpace(acceptVal))
    {
      var s = acceptVal.ToString();
      if (s.StartsWith("uk", StringComparison.OrdinalIgnoreCase)) return new System.Globalization.CultureInfo("uk-UA");
      if (s.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return System.Globalization.CultureInfo.InvariantCulture;
    }

    return new System.Globalization.CultureInfo("uk-UA");
  }

  private static async Task<List<long>> ExtractIdsAsync(HttpRequest request, CancellationToken ct)
  {
    List<long> ids = [];
    if (request.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
    {
      ids.Add(parsedId);
      return ids;
    }

    if (request.ContentLength > 0)
    {
      try
      {
        using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("selectedIds", out var sel) && sel.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in sel.EnumerateArray())
          {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id)) ids.Add(id);
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var sid)) ids.Add(sid);
          }
        }
        else if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
          if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var id)) ids.Add(id);
          else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var sid)) ids.Add(sid);
        }
      }
      catch { }
    }

    return ids;
  }

  private static MenuItemDto? ToMenuItem(string moduleHandle, ModuleMenuNode node, IUserContext user)
  {
    if (node.IsSeparator)
    {
      return new MenuItemDto { IsSeparator = true };
    }

    if (node.Feature is { } feature)
    {
      if (node.IsHidden) return null;
      if (user.IsAuthenticated && (!user.Can(feature.View) || !user.Can(feature.MenuView)))
      {
        return null;
      }

      var modLower = moduleHandle.ToLowerInvariant();
      var fullPath = feature.Path;
      if (fullPath is not null && !fullPath.StartsWith(modLower + "/", StringComparison.OrdinalIgnoreCase))
      {
        fullPath = $"{modLower}/{fullPath.TrimStart('/')}";
      }

      return new MenuItemDto
      {
        Path = fullPath,
        Handle = node.Handle,
        Name = node.Title != null ? Localizer.Instance[node.Title] : null,
        Icon = node.Icon,
        Screen = feature.ServiceType != null ? ServicePathResolver.Resolve(feature.ServiceType) : feature.Path
      };
    }

    var visibleChildren = node.Children
      .Select(c => ToMenuItem(moduleHandle, c, user))
      .Where(c => c != null)
      .Select(c => c!)
      .ToList();

    var trimmedChildren = TrimSeparators(visibleChildren);
    if (trimmedChildren.Count == 0) return null;

    return new MenuItemDto
    {
      Handle = node.Handle,
      Name = node.Title != null ? Localizer.Instance[node.Title] : null,
      Icon = node.Icon,
      Items = trimmedChildren
    };
  }

  private static List<MenuItemDto> TrimSeparators(List<MenuItemDto> items)
  {
    var trimmed = new List<MenuItemDto>();
    foreach (var item in items)
    {
      if (item.IsSeparator && (trimmed.Count == 0 || trimmed[^1].IsSeparator))
      {
        continue;
      }
      trimmed.Add(item);
    }
    if (trimmed.Count > 0 && trimmed[^1].IsSeparator)
    {
      trimmed.RemoveAt(trimmed.Count - 1);
    }
    return trimmed;
  }

  private static IEnumerable<MenuItemDto> Flatten(MenuItemDto item)
  {
    if (item.Items.Count == 0 && item.Path != null)
    {
      yield return item;
    }
    else
    {
      foreach (var child in item.Items)
      {
        foreach (var leaf in Flatten(child))
        {
          yield return leaf;
        }
      }
    }
  }

  private static async Task PopulateFormReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    ScreenDto screen,
    PlatformDbContext db,
    ModuleCatalog catalog,
    CancellationToken ct)
  {
    if (entity == null) return;

    FormModel? formModel = null;
    if (screen.Layout?.Root != null)
    {
      formModel = FindFormModel(screen.Layout.Root);
    }

    if (formModel == null) return;

    var entityProps = entity.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
      .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    foreach (var el in formModel.Elements)
    {
      if (string.IsNullOrEmpty(el.ReferencePath)) continue;

      long? key = null;
      if (dict.TryGetValue(el.Path, out var rawVal) && rawVal != null)
      {
        if (rawVal is JsonElement je)
        {
          if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jeId)) key = jeId;
          else if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var jeParsed)) key = jeParsed;
        }
        else if (rawVal is long l) key = l;
        else if (rawVal is int i) key = i;
        else if (long.TryParse(rawVal.ToString(), out var parsedId)) key = parsedId;
      }

      if (key == null || key <= 0)
      {
        dict[el.ReferencePath] = null;
        continue;
      }

      // 1. Try reading from entity navigation property
      object? navObj = null;
      if (entityProps.TryGetValue(el.ReferencePath, out var navProp))
      {
        navObj = navProp.GetValue(entity);
      }
      else
      {
        var candidateName = el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
          ? el.Path[..^2]
          : el.Path;
        if (entityProps.TryGetValue(candidateName, out navProp))
        {
          navObj = navProp.GetValue(entity);
        }
      }

      string? refName = null;
      if (navObj != null)
      {
        var nameProp = navObj.GetType().GetProperty("Name")
          ?? navObj.GetType().GetProperty("Title")
          ?? navObj.GetType().GetProperty("Number")
          ?? navObj.GetType().GetProperty("Login");
        refName = nameProp?.GetValue(navObj)?.ToString();
      }

      // 2. Fallback to DbContext lookup if navigation property is null
      if (string.IsNullOrEmpty(refName) && el.Model is FormSelectModel selectModel && !string.IsNullOrEmpty(selectModel.ReferenceService))
      {
        var refServiceType = catalog.FindServiceType(selectModel.ReferenceService)
          ?? catalog.FindFeature(selectModel.ReferenceService)?.ServiceType;

        if (refServiceType != null)
        {
          Type? refEntityType = null;
          var curType = refServiceType;
          while (curType != null && curType != typeof(object))
          {
            if (curType.IsGenericType && (curType.GetGenericTypeDefinition() == typeof(GridService<,>) || curType.GetGenericTypeDefinition() == typeof(FormService<,>)))
            {
              refEntityType = curType.GetGenericArguments()[0];
              break;
            }
            curType = curType.BaseType;
          }

          if (refEntityType != null)
          {
            try
            {
              var loaded = await db.FindAsync(refEntityType, [key.Value], ct);
              if (loaded != null)
              {
                var nameProp = loaded.GetType().GetProperty("Name")
                  ?? loaded.GetType().GetProperty("Title")
                  ?? loaded.GetType().GetProperty("Number")
                  ?? loaded.GetType().GetProperty("Login");
                refName = nameProp?.GetValue(loaded)?.ToString();
              }
            }
            catch { }
          }
        }
      }

      dict[el.ReferencePath] = new Reference
      {
        Id = key.Value,
        Name = !string.IsNullOrEmpty(refName) ? refName : $"#{key.Value}"
      };
    }
  }

  private static FormModel? FindFormModel(PanelNodeDto node)
  {
    if (node.Model is FormModel fm) return fm;
    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        var res = FindFormModel(child);
        if (res != null) return res;
      }
    }
    return null;
  }
}
