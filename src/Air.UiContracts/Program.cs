var targetDir = args.Length > 0 ? args[0] : "air-ui/src/core/generated";
Directory.CreateDirectory(targetDir);

var contractsFile = Path.Combine(targetDir, "contracts.ts");
var contractsTs = ContractExporter.GenerateContracts();
await File.WriteAllTextAsync(contractsFile, contractsTs);

var textsFile = Path.Combine(targetDir, "ui-texts.json");
var textsJson = JsonSerializer.Serialize(UiTexts.English, new JsonSerializerOptions
{
  WriteIndented = true
});
await File.WriteAllTextAsync(textsFile, textsJson);

var demoDir = Path.Combine(Directory.GetCurrentDirectory(), "demo/web/apps/demo/src/app/core/generated");
if (Directory.Exists(demoDir))
{
  await File.WriteAllTextAsync(Path.Combine(demoDir, "contracts.ts"), contractsTs);
  await File.WriteAllTextAsync(Path.Combine(demoDir, "ui-texts.json"), textsJson);
}

Console.WriteLine($"[Air.UiContracts] Successfully exported contracts to {targetDir}");
