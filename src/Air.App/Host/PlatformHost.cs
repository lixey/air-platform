using System.Text.Json;
using Air.App.Form;
using Air.App.Grid;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Screens;
using Air.App.TypeScript;
using Air.Data.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    where TModule : class, IPlatformModule, new()
  {
    var module = new TModule();

    // 1. Auto-register module assembly *Service, IGridService, IFormService
    var moduleAssembly = typeof(TModule).Assembly;
    var serviceTypes = moduleAssembly.GetTypes()
      .Where(t => t is { IsClass: true, IsAbstract: false } &&
                  (typeof(IGridService).IsAssignableFrom(t) ||
                   typeof(IFormService).IsAssignableFrom(t) ||
                   t.Name.EndsWith("Service")));

    foreach (var st in serviceTypes)
    {
      builder.Services.TryAddScoped(st);
    }

    // 2. Also register any service types explicitly specified in Menu features
    foreach (var node in ModuleCatalog.Flatten(module.Menu.Nodes))
    {
      if (node.Feature?.ServiceType is { IsClass: true, IsAbstract: false } st)
      {
        builder.Services.TryAddScoped(st);
      }
    }

    // 3. User custom module configuration
    module.ConfigureServices(builder.Services);

    // 4. Common platform services: JSON, Tenant context, Screen catalog
    builder.Services.ConfigureHttpJsonOptions(o => PlatformJsonOptions.ConfigurePlatformJson(o.SerializerOptions));
    builder.Services.AddHttpContextAccessor();
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

  public static void MapPlatform(this WebApplication app)
  {
    var catalog = app.Services.GetRequiredService<ModuleCatalog>();
    ModuleCatalog.Instance = catalog;
    app.MapPlatformEndpoints();
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

    // Register Platform Infrastructure
    var catalog = new ModuleCatalog();
    foreach (var module in options.Modules)
    {
      module.ConfigureServices(builder.Services);
      catalog.Register(module);
    }

    builder.Services.AddSingleton(catalog);
    builder.Services.AddScoped<ScreenCatalog>();

    var app = builder.Build();

    app.MapPlatformEndpoints();
    pipeline?.Invoke(app);

    await app.RunAsync();
  }
}
