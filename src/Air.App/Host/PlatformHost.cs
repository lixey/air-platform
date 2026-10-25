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

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin =>
      {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
          return false;
        }

        return uri.IsLoopback || allowedOrigins.Any(o =>
          string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
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

    await app.RunAsync();
  }
}
