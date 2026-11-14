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
      builder.Services.TryAddSingleton<Auth.Internals.SessionRefreshCoordinator>();
      builder.Services.TryAddSingleton<Mutations.Internals.IdempotencyStore>();
      builder.Services.TryAddSingleton<FileLinks>();
      builder.AddPlatformService<PlatformAuthService>();
      builder.AddPlatformService<PlatformLanguageService>();
      builder.AddPlatformService<PlatformTenantService>();

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

      if (builder.Services.All(d => d.ServiceType != typeof(IFileStorage)))
      {
        builder.AddPlatformLocalFiles();
      }

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
    builder.Services.TryAddSingleton(sp =>
      EndpointRegistry.Build(sp.GetRequiredService<ModuleCatalog>()));

    return builder;
  }

  public static IServiceCollection AddPlatformService<TService>(this IServiceCollection services)
    where TService : class, IPlatformService
  {
    var routeAttr = typeof(TService).GetCustomAttribute<ServiceRouteAttribute>()
      ?? throw new InvalidOperationException(
        $"Platform service '{typeof(TService).FullName}' is missing '[ServiceRoute(...)]'.");
    return services.AddPlatformService<TService>(routeAttr.Route);
  }

  public static IServiceCollection AddPlatformService<TService>(this IServiceCollection services, string route)
    where TService : class, IPlatformService
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(route);
    services.TryAddScoped<TService>(sp =>
    {
      var svc = ActivatorUtilities.CreateInstance<TService>(sp);
      svc.BindContext(sp.GetService<PlatformAppService>());
      return svc;
    });
    var normalizedRoute = route.Trim('/');
    if (services.All(d => d.ImplementationInstance is not ModuleCatalogSetup s || s.Key != normalizedRoute))
    {
      services.AddSingleton(new ModuleCatalogSetup(
        catalog => catalog.RegisterPlatformService<TService>(normalizedRoute),
        normalizedRoute));
    }

    return services;
  }

  public static WebApplicationBuilder AddPlatformService<TService>(this WebApplicationBuilder builder)
    where TService : class, IPlatformService
  {
    builder.Services.AddPlatformService<TService>();
    return builder;
  }

  public static WebApplicationBuilder AddPlatformService<TService>(this WebApplicationBuilder builder, string route)
    where TService : class, IPlatformService
  {
    builder.Services.AddPlatformService<TService>(route);
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

    return app.UseMiddleware<PlatformAuthMiddleware>();
  }

  public static RouteGroupBuilder MapPlatformApi(this WebApplication app)
  {
    app.UsePlatformAuth();
    return app.MapPlatformApiEndpoints();
  }

  public static WebApplicationBuilder AddPlatformLocalFiles(
    this WebApplicationBuilder builder,
    string? path = null)
  {
    ArgumentNullException.ThrowIfNull(builder);
    var resolvedPath = string.IsNullOrWhiteSpace(path) ? builder.Configuration["Files:Path"] : path;
    if (string.IsNullOrWhiteSpace(resolvedPath))
    {
      resolvedPath = Path.Combine(builder.Environment.ContentRootPath, "platform_files");
    }
    else if (!Path.IsPathRooted(resolvedPath))
    {
      resolvedPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, resolvedPath));
    }

    builder.Services.Replace(ServiceDescriptor.Singleton<IFileStorage>(_ => new LocalFileStorage(resolvedPath)));
    builder.Services.TryAddSingleton<IFileStores>(sp => new DefaultFileStores(sp.GetRequiredService<IFileStorage>()));
    return builder;
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
}
