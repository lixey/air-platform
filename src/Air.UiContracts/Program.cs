var targetDir = args.Length > 0 ? args[0] : "air-ui/src/core/generated";
Directory.CreateDirectory(targetDir);

var contractsFile = Path.Combine(targetDir, "contracts.ts");
var contractsTs = ContractExporter.GenerateContracts(typeof(AgentEndpointsExtensions).Assembly);
await File.WriteAllTextAsync(contractsFile, contractsTs);

var textsFile = Path.Combine(targetDir, "ui-texts.json");
var textsJson = JsonSerializer.Serialize(UiTexts.English, new JsonSerializerOptions
{
  WriteIndented = true
});
await File.WriteAllTextAsync(textsFile, textsJson + "\n");

Console.WriteLine($"[Platform.UiContracts] Successfully exported contracts to {targetDir}");
