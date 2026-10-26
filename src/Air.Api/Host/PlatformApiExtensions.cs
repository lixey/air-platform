using Platform.Api.Host.Internals;

namespace Platform.Api.Host;

public static class PlatformApiExtensions
{
  public static WebApplicationBuilder AddPlatformApiModule<TModule>(this WebApplicationBuilder builder)
    where TModule : class, IPlatformModule, new() =>
    builder.AddPlatformApiModule(new TModule());

  public static WebApplicationBuilder AddPlatformApiModule(this WebApplicationBuilder builder, IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);

    var moduleAssembly = module.GetType().Assembly;
    var serviceTypes = moduleAssembly.GetTypes()
      .Where(t => t is { IsClass: true, IsAbstract: false } &&
                  (typeof(IPlatformService).IsAssignableFrom(t) ||
                   typeof(PlatformAppService).IsAssignableFrom(t) ||
                   t.Name.EndsWith("Service")));

    foreach (var st in serviceTypes)
    {
      builder.Services.TryAddScoped(st);
      if (typeof(PlatformAppService).IsAssignableFrom(st) && st != typeof(PlatformAppService))
      {
        builder.Services.TryAddScoped(typeof(PlatformAppService), sp => sp.GetRequiredService(st));
      }
    }

    foreach (var node in ModuleCatalog.Flatten(module.Menu.Nodes))
    {
      if (node.Feature?.ServiceType is { IsClass: true, IsAbstract: false } st)
      {
        builder.Services.TryAddScoped(st);
      }
    }

    foreach (var resType in moduleAssembly.GetTypes().Where(t => t.Name.EndsWith("Resource", StringComparison.OrdinalIgnoreCase) && !t.IsAbstract && !t.IsGenericType))
    {
      Localizer.Instance.Register(resType);
    }

    module.Configure(builder);

    var tokenSecret = builder.Configuration["Auth:TokenSecret"]
      ?? builder.Configuration["Platform:Security:Jwt:Secret"]
      ?? Environment.GetEnvironmentVariable("AIR_TOKEN_SECRET");
    SessionTokenService.ConfigureSecret(tokenSecret);
    builder.Services.ConfigureHttpJsonOptions(o => PlatformJsonOptions.ConfigurePlatformJson(o.SerializerOptions));
    builder.Services.AddHttpContextAccessor();
    builder.Services.TryAddSingleton<ILocalizer>(Localizer.Instance);
    builder.Services.TryAddSingleton<IPasswordHasher, PasswordHasher>();
    if (builder.Services.All(d => d.ServiceType != typeof(Microsoft.Extensions.Caching.Distributed.IDistributedCache)))
    {
      builder.Services.AddDistributedMemoryCache();
    }

    builder.Services.TryAddSingleton<ICacheProvider, Caching.Internals.CacheProvider>();
    builder.Services.TryAddSingleton<IUserAuthCache, Auth.Internals.UserAuthCache>();
    var filesPath = builder.Configuration["Files:Path"];
    if (string.IsNullOrWhiteSpace(filesPath))
    {
      filesPath = Path.Combine(builder.Environment.ContentRootPath, "air_files");
    }
    else if (!Path.IsPathRooted(filesPath))
    {
      filesPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, filesPath));
    }

    builder.Services.TryAddSingleton<IFileStores>(_ => new DefaultFileStores(new LocalFileStorage(filesPath)));
    builder.Services.TryAddScoped<IUserContext, UserContext>();
    builder.Services.TryAddScoped<PlatformAppService>();
    builder.Services.TryAddScoped<ITenantContext, HttpTenantContext>();

    builder.Services.AddSingleton<IPlatformModule>(module);
    builder.Services.TryAddSingleton(sp =>
    {
      var catalog = new ModuleCatalog();
      foreach (var m in sp.GetServices<IPlatformModule>())
      {
        catalog.Register(m);
      }

      ModuleCatalog.Instance = catalog;
      return catalog;
    });

    return builder;
  }

  public static IApplicationBuilder UsePlatformAuth(this IApplicationBuilder app)
  {
    return app.Use(async (context, next) =>
    {
      string? token = null;
      var isCookie = false;

      if (context.Request.Cookies.TryGetValue(AuthClaims.AccessCookie, out var cookieToken) &&
          !string.IsNullOrWhiteSpace(cookieToken))
      {
        token = cookieToken;
        isCookie = true;
      }
      else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
               authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
      {
        token = authHeader.ToString()["Bearer ".Length..].Trim();
      }

      if (!string.IsNullOrWhiteSpace(token) &&
          SessionTokenService.TryValidateAccessToken(token, out var sessionId, out var userId, out _, out var tokenVersion) &&
          context.RequestServices.GetService<IAuthStore>() is { } authStore &&
          context.RequestServices.GetService<IUserAuthCache>() is { } authCache)
      {
        var session = await authCache.GetSessionAsync(sessionId, authStore, context.RequestAborted);
        if (session != null && session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > DateTimeOffset.UtcNow)
        {
          var user = await authCache.GetUserProfileAsync(userId, session.TenantId, authStore, context.RequestServices, context.RequestAborted);
          if (user != null && user.IsActive && user.TokenVersion == tokenVersion)
          {
            var claims = new List<Claim>(6)
            {
              new(AuthClaims.Sub, user.Id.ToString()),
              new(AuthClaims.Login, user.Login),
              new(AuthClaims.SessionId, session.Id.ToString()),
              new(AuthClaims.Version, user.TokenVersion.ToString())
            };

            if (user.MustChangePassword)
            {
              claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
            }

            var tenantId = session.TenantId ?? user.HomeTenantId;
            if (tenantId is > 0)
            {
              claims.Add(new Claim(AuthClaims.Tenant, tenantId.Value.ToString()));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "PlatformAuth"));
            context.Items[AuthClaims.SnapshotItem] = user;

            if (isCookie)
            {
              context.Items[AuthClaims.ViaCookieItem] = true;
            }

            using (AsyncLocalTenantContext.Use(tenantId, user.PermissionSet, user.Id, context.TraceIdentifier))
            {
              await next();
              return;
            }
          }
        }
      }

      await next();
    });
  }

  public static RouteGroupBuilder MapPlatformApi(this WebApplication app)
  {
    var catalog = app.Services.GetRequiredService<ModuleCatalog>();
    ModuleCatalog.Instance = catalog;
    app.UsePlatformAuth();
    return app.MapPlatformApiEndpoints();
  }

  public static WebApplicationBuilder AddPlatformJob<TJob>(
    this WebApplicationBuilder builder,
    TimeSpan interval,
    TimeSpan? initialDelay = null)
    where TJob : class, IPlatformJob
  {
    builder.Services.TryAddScoped<TJob>();
    builder.Services.AddSingleton(new PlatformJobRegistration(typeof(TJob), interval, initialDelay));
    builder.Services.AddHostedService<PlatformJobHostedService>();
    return builder;
  }
}
