namespace Platform.App.Host;

public static class PlatformHost
{
  public const string ExportContractsSwitch = "--export-ui-contracts";

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

    foreach (var builderAction in options.BuilderActions)
    {
      builderAction(builder);
    }

    builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin =>
      {
        var uri = new Uri(origin);
        return uri.IsLoopback || uri.Host.EndsWith("privat24.ua", StringComparison.OrdinalIgnoreCase);
      })
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials()));

    var app = builder.Build();
    app.UseCors();
    app.MapPlatform();

    foreach (var appAction in options.AppActions)
    {
      appAction(app);
    }

    pipeline?.Invoke(app);

    try
    {
      await using var scope = app.Services.CreateAsyncScope();
      var db = scope.ServiceProvider.GetService<PlatformDbContext>();
      if (db is not null)
      {
        await AgentModelCatalog.EnsureSeededAsync(db);
        await AgentMemoryCatalog.EnsureSeededAsync(db);
      }
    }
    catch
    {
      // Non-fatal if DB is not yet migrated on startup
    }

    await app.RunAsync();
  }
}
