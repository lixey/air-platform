using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace Air.App.Host;

public sealed class PlatformOptions
{
  private readonly List<IPlatformModule> _modules = [];

  public IReadOnlyList<IPlatformModule> Modules => _modules;

  public PlatformOptions AddModule<TModule>() where TModule : IPlatformModule, new()
  {
    _modules.Add(new TModule());
    return this;
  }

  public PlatformOptions AddModule(IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);
    _modules.Add(module);
    return this;
  }
}

public static class PlatformHost
{
  public const string ExportContractsSwitch = "--export-ui-contracts";

  public static WebApplicationBuilder AddPlatformModule<TModule>(this WebApplicationBuilder builder)
    where TModule : class, IPlatformModule, new() =>
    builder.AddPlatformModule(new TModule());

  public static WebApplicationBuilder AddPlatformModule(this WebApplicationBuilder builder, IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);

    // 1. Auto-register module assembly *Service, IGridService, IFormService, IScreenService
    var moduleAssembly = module.GetType().Assembly;
    var serviceTypes = moduleAssembly.GetTypes()
      .Where(t => t is { IsClass: true, IsAbstract: false } &&
                  (typeof(IGridService).IsAssignableFrom(t) ||
                   typeof(IFormService).IsAssignableFrom(t) ||
                   typeof(IScreenService).IsAssignableFrom(t) ||
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

    // 2. Also register any service types explicitly specified in Menu features
    foreach (var node in ModuleCatalog.Flatten(module.Menu.Nodes))
    {
      if (node.Feature?.ServiceType is { IsClass: true, IsAbstract: false } st)
      {
        builder.Services.TryAddScoped(st);
      }
    }

    // 3. Register module ResourceManager if present in module assembly
    foreach (var resType in moduleAssembly.GetTypes().Where(t => t.Name.EndsWith("Resource", StringComparison.OrdinalIgnoreCase) && !t.IsAbstract && !t.IsGenericType))
    {
      Localizer.Instance.Register(resType);
    }

    // 4. User custom module configuration
    module.Configure(builder);

    // 5. Common platform services: JSON, Tenant context, User context, Localizer, AppService
    SessionTokenService.ConfigureSecret(builder.Configuration["Auth:TokenSecret"]);
    builder.Services.ConfigureHttpJsonOptions(o => PlatformJsonOptions.ConfigurePlatformJson(o.SerializerOptions));
    builder.Services.AddHttpContextAccessor();
    builder.Services.TryAddSingleton<ILocalizer>(Localizer.Instance);
    builder.Services.TryAddSingleton<IPasswordHasher, PasswordHasher>();
    var filesPath = builder.Configuration["Files:Path"];
    if (string.IsNullOrWhiteSpace(filesPath))
    {
      filesPath = Path.Combine(builder.Environment.ContentRootPath, "air_files");
    }
    else if (!Path.IsPathRooted(filesPath))
    {
      filesPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, filesPath));
    }

    builder.Services.TryAddSingleton<Files.IFileStores>(_ => new Files.DefaultFileStores(new Data.Files.LocalFileStorage(filesPath)));
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
    builder.Services.AddScoped<ScreenCatalog>();
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
          SessionTokenService.TryValidateAccessToken(token, out var sessionId, out var userId, out _, out var tokenVersion))
      {
        var authStore = context.RequestServices.GetService<IAuthStore>();
        if (authStore != null)
        {
          var session = await authStore.FindSessionAsync(sessionId, context.RequestAborted);
          if (session != null && session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > DateTimeOffset.UtcNow)
          {
            var user = await authStore.FindUserByIdAsync(userId, context.RequestAborted);
            if (user != null && user.IsActive && user.TokenVersion == tokenVersion)
            {
              var sessionTenantId = session.TenantId ?? (user as IHasTenant)?.TenantId;
              SecurityInfo securityInfo;
              using (AsyncLocalTenantContext.Use(sessionTenantId, null, user.Id, context.TraceIdentifier))
              {
                securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, context.RequestAborted);
              }

              var claims = new List<Claim>
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

              var tenantId = sessionTenantId ?? securityInfo.HomeTenantId;
              if (tenantId is > 0)
              {
                claims.Add(new Claim(AuthClaims.Tenant, tenantId.Value.ToString()));
              }

              foreach (var p in securityInfo.Permissions)
              {
                claims.Add(new Claim(AuthClaims.Permission, p));
              }

              var identity = new ClaimsIdentity(claims, "PlatformAuth");
              context.User = new ClaimsPrincipal(identity);

              if (isCookie)
              {
                context.Items[AuthClaims.ViaCookieItem] = true;
              }

              var rightsSet = securityInfo.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
              using (AsyncLocalTenantContext.Use(tenantId, rightsSet, user.Id, context.TraceIdentifier))
              {
                await next();
                return;
              }
            }
          }
        }
      }

      await next();
    });
  }

  public static void MapPlatform(this WebApplication app)
  {
    var catalog = app.Services.GetRequiredService<ModuleCatalog>();
    ModuleCatalog.Instance = catalog;
    app.UsePlatformAuth();
    app.MapPlatformEndpoints();

    var webRoot = new[]
      {
        app.Environment.WebRootPath,
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot")
      }
      .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(Path.Combine(p, "index.html")));

    if (webRoot != null)
    {
      var provider = new PhysicalFileProvider(Path.GetFullPath(webRoot));
      var indexFile = Path.Combine(provider.Root, "index.html");
      app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
      app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
      app.MapFallback(context =>
      {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
          context.Response.StatusCode = StatusCodes.Status404NotFound;
          return Task.CompletedTask;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.SendFileAsync(indexFile);
      });
    }
  }

  public static async Task RunAsync(
    string[] args,
    Action<PlatformOptions> configure,
    Action<WebApplication>? pipeline = null)
  {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(configure);

    var options = new PlatformOptions();
    configure(options);

    // Fast CLI Contract Export Mode
    var exportIndex = Array.IndexOf(args, ExportContractsSwitch);
    if (exportIndex >= 0 && exportIndex + 1 < args.Length)
    {
      var outputDir = Path.GetFullPath(args[exportIndex + 1]);
      Directory.CreateDirectory(outputDir);

      var assemblies = options.Modules.Select(m => m.GetType().Assembly).ToArray();
      var contractsTs = ContractExporter.GenerateContracts(assemblies);
      var contractsFile = Path.Combine(outputDir, "contracts.ts");
      await File.WriteAllTextAsync(contractsFile, contractsTs);
      Console.WriteLine($"[Air.Platform] Generated UI contracts: {contractsFile}");

      var textsFile = Path.Combine(outputDir, "ui-texts.json");
      var textsJson = JsonSerializer.Serialize(UiTexts.English, new JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(textsFile, textsJson + "\n");
      Console.WriteLine($"[Air.Platform] Generated UI texts: {textsFile}");
      return;
    }

    var builder = WebApplication.CreateBuilder(args);

    foreach (var module in options.Modules)
    {
      builder.AddPlatformModule(module);
    }

    var app = builder.Build();
    app.MapPlatform();
    pipeline?.Invoke(app);

    await app.RunAsync();
  }
}
