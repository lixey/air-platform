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

      var assemblies = options.Modules
        .Select(static m => m.GetType().Assembly)
        .Concat(options.ContractAssemblies)
        .Distinct()
        .ToArray();
      var contractsTs = ContractExporter.GenerateContracts(assemblies);
      var contractsFile = Path.Combine(outputDir, "contracts.ts");
      await File.WriteAllTextAsync(contractsFile, contractsTs);
      Console.WriteLine($"[Platform] Generated UI contracts: {contractsFile}");

      var textsFile = Path.Combine(outputDir, "ui-texts.json");
      var textsJson = JsonSerializer.Serialize(UiTexts.English, new JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(textsFile, textsJson + "\n");
      Console.WriteLine($"[Platform] Generated UI texts: {textsFile}");
      return;
    }

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSingleton(options);

    foreach (var module in options.Modules)
    {
      builder.AddPlatformModule(module);
    }

    foreach (var builderAction in options.BuilderActions)
    {
      builderAction(builder);
    }

    var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
      .Select(o => o?.Trim().TrimEnd('/'))
      .Where(o => !string.IsNullOrEmpty(o))
      .Select(o => o!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();

    foreach (var origin in allowedOrigins)
    {
      if (origin == "*")
      {
        throw new InvalidOperationException(
          "Wildcard '*' is forbidden in Cors:AllowedOrigins when credentials are enabled. Specify explicit origins.");
      }

      if (builder.Environment.IsProduction() &&
          (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) || parsedOrigin.IsLoopback))
      {
        throw new InvalidOperationException(
          $"Invalid Cors:AllowedOrigins entry '{origin}' in Production. Loopback and relative origins are forbidden.");
      }
    }

    if (allowedOrigins.Length > 0)
    {
      builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
    }

    var app = builder.Build();
    app.UsePlatformForwardedHeaders();
    if (allowedOrigins.Length > 0)
    {
      app.UseCors();
    }

    app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
    app.MapPlatform();

    foreach (var appAction in options.AppActions)
    {
      appAction(app);
    }

    pipeline?.Invoke(app);

    await app.RunAsync();
  }
}
