using Microsoft.AspNetCore.HttpOverrides;
using Platform.Api.Host.Internals;

namespace Platform.Api.Host;

public static class PlatformApiExtensions
{
  private const string ForwardedHeadersRegisteredKey = "Platform.ForwardedHeadersRegistered";
  private const string SecurityPipelineRegisteredKey = "Platform.SecurityPipelineRegistered";
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
      if (typeof(IPlatformService).IsAssignableFrom(st))
      {
        var captured = st;
        builder.Services.TryAddScoped(captured, sp =>
        {
          var svc = ActivatorUtilities.CreateInstance(sp, captured);
          (svc as IPlatformService)?.BindContext(sp.GetService<PlatformAppService>());
          return svc;
        });
      }
      else
      {
        builder.Services.TryAddScoped(st);
      }

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
      ?? Environment.GetEnvironmentVariable(SessionTokenService.SecretEnvironmentVariable);
    SessionTokenService.EnsureConfiguredForEnvironment(tokenSecret, builder.Environment.IsProduction());

    if (builder.Services.All(d => d.ServiceType != typeof(PlatformApiCoreMarker)))
    {
      builder.Services.AddSingleton<PlatformApiCoreMarker>();
      builder.Services.TryAddSingleton(_ => BuildDefaultPlatformOptions(builder.Configuration));
      builder.Services.ConfigureHttpJsonOptions(o => PlatformJsonOptions.ConfigurePlatformJson(o.SerializerOptions));
      builder.Services.AddHttpContextAccessor();
      builder.Services.TryAddSingleton<ILocalizer>(Localizer.Instance);
      builder.Services.TryAddSingleton<ICultureCatalog>(sp =>
      {
        var opts = sp.GetRequiredService<PlatformOptions>();
        var catalog = opts.Cultures.Count > 0
          ? new CultureCatalog(opts.Cultures, opts.DefaultCulture)
          : CultureCatalog.CreateDefault(opts.DefaultCulture);
        Localizer.Instance.ConfigureCultures(catalog);
        return catalog;
      });
      builder.Services.TryAddSingleton<IPasswordHasher, PasswordHasher>();
      builder.Services.AddHybridCache();
      builder.Services.TryAddSingleton<ICacheProvider, Caching.Internals.CacheProvider>();
      builder.Services.TryAddSingleton<IUserAuthCache>(sp =>
        new Auth.Internals.UserAuthCache(
          sp.GetRequiredService<ICacheProvider>(),
          sp.GetRequiredService<IServiceScopeFactory>()));
      builder.Services.TryAddSingleton<Auth.Internals.LoginRateLimiter>();
      builder.Services.TryAddSingleton<Mutations.Internals.IdempotencyStore>();
      builder.Services.TryAddSingleton<FileLinks>();

      var hasExplicitForwardedOption = builder.Services.Any(d =>
        d.ServiceType == typeof(PlatformOptions) &&
        d.ImplementationInstance is PlatformOptions { UseForwardedHeaders: true });

      if (hasExplicitForwardedOption || IsForwardedHeadersConfigured(builder.Configuration))
      {
        var parsedProxies = new List<System.Net.IPAddress>();
        var parsedNetworks = new List<System.Net.IPNetwork>();
        if (builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() is { Length: > 0 } proxies)
        {
          foreach (var rawProxy in proxies)
          {
            var trimmed = rawProxy?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
              throw new InvalidOperationException("ForwardedHeaders:KnownProxies cannot contain empty entries.");
            }

            if (System.Net.IPAddress.TryParse(trimmed, out var ip))
            {
              parsedProxies.Add(ip);
            }
            else if (System.Net.IPNetwork.TryParse(trimmed, out var network))
            {
              parsedNetworks.Add(network);
            }
            else
            {
              throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownProxies entry '{rawProxy}'. Expected an IP address or CIDR network.");
            }
          }
        }

        builder.Services.Configure<ForwardedHeadersOptions>(fwd =>
        {
          fwd.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
          fwd.ForwardLimit = builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
          if (parsedProxies.Count > 0 || parsedNetworks.Count > 0)
          {
            fwd.KnownProxies.Clear();
            fwd.KnownIPNetworks.Clear();
            foreach (var ip in parsedProxies)
            {
              fwd.KnownProxies.Add(ip);
            }

            foreach (var network in parsedNetworks)
            {
              fwd.KnownIPNetworks.Add(network);
            }
          }
        });
      }

      var filesPath = builder.Configuration["Files:Path"];
      if (string.IsNullOrWhiteSpace(filesPath))
      {
        filesPath = Path.Combine(builder.Environment.ContentRootPath, "platform_files");
      }
      else if (!Path.IsPathRooted(filesPath))
      {
        filesPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, filesPath));
      }

      builder.Services.TryAddSingleton<IFileStorage>(_ => new LocalFileStorage(filesPath));
      builder.Services.TryAddSingleton<IFileStores>(sp => new DefaultFileStores(sp.GetRequiredService<IFileStorage>()));
      builder.Services.TryAddScoped<IUserContext, UserContext>();
      builder.Services.TryAddScoped<PlatformAppService>();
      builder.Services.TryAddScoped<ITenantContext, HttpTenantContext>();
    }

    builder.Services.AddSingleton<IPlatformModule>(module);
    builder.Services.TryAddSingleton(sp =>
    {
      var catalog = new ModuleCatalog();
      ModuleCatalog.Instance = catalog;
      foreach (var m in sp.GetServices<IPlatformModule>())
      {
        catalog.Register(m);
      }

      foreach (var setup in sp.GetServices<ModuleCatalogSetup>())
      {
        setup.Configure(catalog);
      }

      return catalog.Freeze();
    });
    builder.Services.TryAddSingleton(sp => EndpointRegistry.Build(sp.GetRequiredService<ModuleCatalog>()));

    return builder;
  }

  public static IApplicationBuilder UsePlatformForwardedHeaders(this IApplicationBuilder app)
  {
    var options = app.ApplicationServices.GetService<PlatformOptions>();
    var config = app.ApplicationServices.GetService<IConfiguration>();
    if ((options?.UseForwardedHeaders == true || config != null && IsForwardedHeadersConfigured(config)) &&
        app.Properties.TryAdd(ForwardedHeadersRegisteredKey, true))
    {
      var configuredFwd = app.ApplicationServices.GetService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>()?.Value;
      if (configuredFwd is { ForwardedHeaders: not ForwardedHeaders.None })
      {
        app.UseForwardedHeaders();
      }
      else
      {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
          ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
          ForwardLimit = config?.GetValue("ForwardedHeaders:ForwardLimit", 1) ?? 1
        });
      }
    }

    return app;
  }

  public static IApplicationBuilder UsePlatformAuth(this IApplicationBuilder app)
  {
    app.UsePlatformForwardedHeaders();
    if (app.Properties.TryAdd(SecurityPipelineRegisteredKey, true))
    {
      app.UseMiddleware<SafeExceptionMiddleware>();
      app.UseMiddleware<SecurityHeadersMiddleware>();
    }

    return app.Use(async (context, next) =>
    {
      if (context.RequestServices.GetService<ModuleCatalog>() is { } requestCatalog)
      {
        ServiceEndpointsExtensions.TryBindModuleDbScope(context.Request.Path, requestCatalog, context.RequestServices);
      }

      string? token = null;
      var isCookie = false;

      var accessCookieName = AuthClaims.GetAccessCookieName(context.Request.Host.Port);
      if (context.Request.Cookies.TryGetValue(accessCookieName, out var cookieToken) &&
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

      var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
      if (!string.IsNullOrWhiteSpace(token) &&
          SessionTokenService.TryValidateAccessToken(token, out var sessionId, out var userId, out _, out var tokenVersion, timeProvider) &&
          context.RequestServices.GetService<IUserAuthCache>() is { } authCache &&
          HasAuthStoreRegistered(context.RequestServices, authCache, out var fallbackStore))
      {
        var now = timeProvider.GetUtcNow();
        var session = await authCache.GetSessionAsync(sessionId, fallbackStore, context.RequestAborted);
        if (session != null && session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now)
        {
          var user = await authCache.GetUserProfileAsync(userId, session.TenantId, fallbackStore, context.RequestAborted);
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
              if (!HttpMethods.IsGet(context.Request.Method) &&
                  !HttpMethods.IsHead(context.Request.Method) &&
                  !HttpMethods.IsOptions(context.Request.Method) &&
                  !context.Request.Headers.ContainsKey(AuthClaims.ClientHeader))
              {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
              }
            }

            if (user.MustChangePassword)
            {
              var reqPath = context.Request.Path.Value ?? "";
              if (reqPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                  !reqPath.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
              {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
              }
            }

            using (AsyncLocalTenantContext.Use(tenantId, user.PermissionSet, user.Id, context.TraceIdentifier))
            {
              await next();
              return;
            }
          }
        }
      }

      using (AsyncLocalTenantContext.Use(null, null, null, context.TraceIdentifier))
      {
        await next();
      }
    });
  }

  public static RouteGroupBuilder MapPlatformApi(this WebApplication app)
  {
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

  private static PlatformOptions BuildDefaultPlatformOptions(IConfiguration configuration)
  {
    var options = new PlatformOptions
    {
      UseForwardedHeaders = IsForwardedHeadersConfigured(configuration)
    };

    if (TimeSpan.TryParse(configuration["Auth:AccessTokenLifetime"], CultureInfo.InvariantCulture, out var accessLifetime) &&
        accessLifetime > TimeSpan.Zero)
    {
      options.AccessTokenLifetime = accessLifetime;
    }

    if (TimeSpan.TryParse(configuration["Auth:RefreshTokenLifetime"], CultureInfo.InvariantCulture, out var refreshLifetime) &&
        refreshLifetime > TimeSpan.Zero)
    {
      options.RefreshTokenLifetime = refreshLifetime;
    }

    return options;
  }

  private static bool IsForwardedHeadersConfigured(IConfiguration configuration) =>
    configuration.GetValue<bool>("ForwardedHeaders:Enabled") ||
    string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);

  private static bool HasAuthStoreRegistered(
    IServiceProvider requestServices,
    IUserAuthCache authCache,
    out IAuthStore? fallbackStore)
  {
    if (authCache is Auth.Internals.UserAuthCache &&
        requestServices.GetService<IServiceProviderIsService>() is { } serviceCheck)
    {
      fallbackStore = null;
      return serviceCheck.IsService(typeof(IAuthStore));
    }

    fallbackStore = requestServices.GetService<IAuthStore>();
    return fallbackStore != null;
  }
}
