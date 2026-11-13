namespace Platform.Arch.Tests;

public sealed class SourceLayoutAndConventionsTests
{
  [Fact]
  public void CSharpSourceFiles_ObeyLayoutNamespaceAndConventionRules()
  {
    var root = FindRepoRoot();
    var csprojs = LoadProjectRootNamespaces(root);
    var errors = new List<string>();

    var airNsRegex = new Regex(@"^\s*namespace\s+Air\b", RegexOptions.Multiline);
    var nsRegex = new Regex(@"^\s*namespace\s+([A-Za-z0-9_.]+)\s*;", RegexOptions.Multiline);
    var topTypeRegex = new Regex(
      @"^(?:public|internal|file)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+([A-Za-z0-9_]+)",
      RegexOptions.Multiline);
    var extensionRegex = new Regex(@"static\s+[^\(\n]+\(\s*this\s+");
    var internalTypeRegex = new Regex(
      @"^internal\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+",
      RegexOptions.Multiline);
    var publicTypeRegex = new Regex(
      @"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+",
      RegexOptions.Multiline);
    var contractTypeRegex = new Regex(
      @"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+[A-Za-z0-9_]+(?:Dto|Request)\b",
      RegexOptions.Multiline);
    var allowAnonRegex = new Regex(@"\[\s*AllowAnonymous\b");
    var newParentHooksRegex = new Regex(@"new\s+(?:FormParent|FormHooks|GridParent|GridHooks)\b");
    var rawClockRegex = new Regex(@"\bDateTime(?:Offset)?\.(?:UtcNow|Now|Today)\b");
    var globalInternalsRegex = new Regex(@"^global\s+using\s+.*\.Internals\b", RegexOptions.Multiline);

    var forbiddenPassword = "Password" + "123";
    var forbiddenAirLiteral = "\"Air" + ".";
    var forbiddenSrcRegex = new Regex(
      "\"(?:mcc" + "Code|Doc" + "Status|counterparty" + "Id|toAccount" + "Id)\"|--dangerously-" + "skip-permissions|" + forbiddenPassword,
      RegexOptions.IgnoreCase);

    foreach (var fullPath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
      var rel = ToRelPath(root, fullPath);
      if (IsIgnoredPath(rel))
      {
        continue;
      }

      var fileName = Path.GetFileName(fullPath);
      var txt = File.ReadAllText(fullPath);

      if (fileName == "GlobalUsings.cs")
      {
        if (globalInternalsRegex.IsMatch(txt))
        {
          errors.Add($"{rel}: forbidden .Internals in GlobalUsings.cs (use local using in .cs files)");
        }

        continue;
      }

      if (fileName == "Program.cs" || fileName.EndsWith(".Designer.cs", StringComparison.Ordinal))
      {
        continue;
      }

      if (airNsRegex.IsMatch(txt))
      {
        errors.Add($"{rel}: forbidden Air namespace");
      }

      if (nsRegex.Match(txt) is { Success: true } nsMatch && TryFindOwningProject(fullPath, csprojs, out var projDir, out var rootNs))
      {
        var expectedNs = ComputeExpectedNamespace(fullPath, projDir, rootNs);
        var actualNs = nsMatch.Groups[1].Value;
        if (actualNs != expectedNs)
        {
          errors.Add($"{rel}: namespace {actualNs} != expected {expectedNs}");
        }
      }

      var inMigrations = rel.Contains("/Migrations/", StringComparison.Ordinal);
      if (!inMigrations)
      {
        var distinctTopTypes = topTypeRegex.Matches(txt).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count();
        if (distinctTopTypes > 1)
        {
          errors.Add($"{rel}: multiple top-level types");
        }
      }

      if (extensionRegex.IsMatch(txt) && !rel.EndsWith("Extensions.cs", StringComparison.Ordinal))
      {
        errors.Add($"{rel}: extension method in non-Extensions file");
      }

      var inSrc = rel.StartsWith("src/", StringComparison.Ordinal);
      var inDemoSrc = rel.StartsWith("demo/src/", StringComparison.Ordinal);
      var inInternals = rel.Contains("/Internals/", StringComparison.Ordinal);

      if (inSrc && internalTypeRegex.IsMatch(txt) && !inInternals)
      {
        errors.Add($"{rel}: internal type must be in Internals/");
      }

      if ((inSrc || inDemoSrc) && inInternals && publicTypeRegex.IsMatch(txt))
      {
        errors.Add($"{rel}: public type must not be in Internals/");
      }

      if (inSrc && contractTypeRegex.IsMatch(txt) && !rel.Contains("/Contracts/", StringComparison.Ordinal))
      {
        errors.Add($"{rel}: contract type must be in Contracts/");
      }

      if ((inSrc || txt.Contains(forbiddenPassword, StringComparison.OrdinalIgnoreCase)) && forbiddenSrcRegex.Match(txt) is { Success: true } badMatch)
      {
        errors.Add($"{rel}: forbidden domain hardcoding {badMatch.Value}");
      }

      if ((rel.EndsWith("GridService.cs", StringComparison.Ordinal) || rel.EndsWith("FormService.cs", StringComparison.Ordinal)) &&
          allowAnonRegex.IsMatch(txt))
      {
        errors.Add($"{rel}: [AllowAnonymous] is forbidden on GridService/FormService; use [WebhookEndpoint] with explicit auth");
      }

      if (inSrc && fileName is not ("FormService.cs" or "GridService.cs") && newParentHooksRegex.IsMatch(txt))
      {
        errors.Add($"{rel}: use GetParent()/GetHooks() instead of new Parent()/new Hooks()");
      }

      if (!inMigrations &&
          !rel.Contains("/Demo.DevMigrations/", StringComparison.Ordinal) &&
          (rel.StartsWith("src/Air.App/", StringComparison.Ordinal) ||
           rel.StartsWith("src/Air.Mcp/", StringComparison.Ordinal) ||
           rel.StartsWith("src/Air.Agent/", StringComparison.Ordinal) ||
           rel.StartsWith("src/Air.UiContracts/", StringComparison.Ordinal) ||
           inDemoSrc) &&
          rawClockRegex.IsMatch(txt))
      {
        errors.Add($"{rel}: use TimeProvider / Time.Today / Time.UtcNow instead of raw DateTime/DateTimeOffset clock reads");
      }

      if (inSrc && txt.Contains(forbiddenAirLiteral, StringComparison.Ordinal))
      {
        errors.Add($"{rel}: forbidden Air.* string literal in platform source");
      }

      if ((inSrc || inDemoSrc) && !inMigrations)
      {
        var lineCap = rel.StartsWith("src/Air.Api/", StringComparison.Ordinal)
          ? 650
          : rel.StartsWith("src/Air.App/", StringComparison.Ordinal)
            ? 800
            : 450;
        var lineCount = CountLines(txt);
        if (lineCount > lineCap)
        {
          errors.Add($"{rel}: exceeds {lineCap} lines ({lineCount})");
        }
      }
    }

    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
  }

  [Fact]
  public void TypeScriptResourcesAndZeroExternalLeakage_ObeyPlatformInvariants()
  {
    var root = FindRepoRoot();
    var errors = new List<string>();

    var paletteRegex = new Regex(
      @"\b(?:dark:|(?:text|bg|border|ring|from|to|via)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d+)");
    var rawRandomUuid = "crypto." + "randomUUID";
    var bypassTrust = "bypassSecurity" + "Trust";

    var tsFiles = Directory.EnumerateFiles(Path.Combine(root, "air-ui/src"), "*.ts", SearchOption.AllDirectories)
      .Concat(Directory.EnumerateFiles(Path.Combine(root, "demo/web/apps"), "*.ts", SearchOption.AllDirectories))
      .Order(StringComparer.Ordinal);

    foreach (var tsPath in tsFiles)
    {
      var rel = ToRelPath(root, tsPath);
      if (rel.EndsWith(".spec.ts", StringComparison.Ordinal) || rel.EndsWith("contracts.ts", StringComparison.Ordinal))
      {
        continue;
      }

      var fileName = Path.GetFileName(tsPath);
      var txt = File.ReadAllText(tsPath);
      var inGenerated = rel.Contains("/generated/", StringComparison.Ordinal);

      if (rel.StartsWith("air-ui/src/", StringComparison.Ordinal) && !inGenerated)
      {
        var lineCount = CountLines(txt);
        if (lineCount > 450)
        {
          errors.Add($"{rel}: exceeds 450 lines ({lineCount})");
        }
      }

      if (fileName != "uuid.ts" && txt.Contains(rawRandomUuid, StringComparison.Ordinal))
      {
        errors.Add($"{rel}: use newUuid() instead of raw {rawRandomUuid}");
      }

      if (txt.Contains(bypassTrust, StringComparison.Ordinal))
      {
        errors.Add($"{rel}: forbidden {bypassTrust}* usage (use native DOM sanitization or structured UI blocks)");
      }

      if (fileName != "ui-color.ts" && !inGenerated && paletteRegex.Match(txt) is { Success: true } palMatch)
      {
        errors.Add($"{rel}: forbidden non-semantic Tailwind palette or dark: class ({palMatch.Value})");
      }
    }

    var resxDataRegex = new Regex(@"<data\s+name=""([^""]+)""");
    var cyrillicNeutralRegex = new Regex(@"<data\s+name=""(?!Ukrainian"")[^""]+""[^>]*>\s*<value>[^<]*[\u0400-\u04FF]");
    foreach (var rxPath in Directory.EnumerateFiles(root, "*.resx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
      var rel = ToRelPath(root, rxPath);
      if (IsIgnoredPath(rel) || rel.EndsWith(".uk.resx", StringComparison.Ordinal))
      {
        continue;
      }

      var ukPath = rxPath[..^5] + ".uk.resx";
      if (!File.Exists(ukPath))
      {
        continue;
      }

      var baseTxt = File.ReadAllText(rxPath);
      var ukTxt = File.ReadAllText(ukPath);
      var baseKeys = resxDataRegex.Matches(baseTxt).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
      var ukKeys = resxDataRegex.Matches(ukTxt).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

      var missingInUk = baseKeys.Except(ukKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
      if (missingInUk.Length > 0)
      {
        errors.Add($"{rel}: missing in .uk.resx: [{string.Join(", ", missingInUk)}]");
      }

      var missingInBase = ukKeys.Except(baseKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
      if (missingInBase.Length > 0)
      {
        errors.Add($"{rel}: missing in neutral .resx: [{string.Join(", ", missingInBase)}]");
      }

      if (cyrillicNeutralRegex.IsMatch(baseTxt))
      {
        errors.Add($"{rel}: Cyrillic text in neutral .resx (must be English; put Ukrainian in .uk.resx)");
      }
    }

    var uiComponentsTxt = File.ReadAllText(Path.Combine(root, "air-ui/src/ui-components.ts"));
    var uiKeys = Regex.Matches(uiComponentsTxt, @"^\s*([A-Za-z0-9_]+)\s*:", RegexOptions.Multiline)
      .Select(m => m.Groups[1].Value)
      .ToHashSet(StringComparer.Ordinal);
    var compModelRegex = new Regex(@"public\s+(?:sealed\s+)?class\s+([A-Za-z0-9_]+)Model\s*:\s*ComponentModel\b");
    var kindRegex = new Regex(@"override\s+string\s+Kind\s*=>\s*""([^""]+)""");

    foreach (var csPath in Directory.EnumerateFiles(Path.Combine(root, "src/Air.App"), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
      var rel = ToRelPath(root, csPath);
      if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
      {
        continue;
      }

      var txt = File.ReadAllText(csPath);
      if (compModelRegex.Match(txt) is { Success: true } modelMatch)
      {
        var kind = kindRegex.Match(txt) is { Success: true } kindMatch ? kindMatch.Groups[1].Value : modelMatch.Groups[1].Value;
        if (!uiKeys.Contains(kind))
        {
          errors.Add($"{rel}: ComponentModel kind {kind} missing in air-ui/src/ui-components.ts");
        }
      }
    }

    var externalAppRegex = new Regex(
      @"\b(?:(?!Demo|Platform|Air|Base|Test|Mock|App|Org|Ecom|Module|Configure|Web|Ui|Dev|Create|Add|Get|Set|Map|Use|Host|Client|InMemory|Sample)[A-Z][a-z0-9]+(?:App|Org|Ecom|Fin|DbContext|Migrations|Transfer)|509[2-9]|519[2-9]|430[1-9]|440[1-9])\b");
    string[] scanDirs = ["src", "air-ui/src", "docs", "tests"];
    var filesToScan = scanDirs
      .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
      .Append(Path.Combine(root, "AGENTS.md"))
      .Append(Path.Combine(root, "Makefile"))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal);

    foreach (var path in filesToScan)
    {
      var rel = ToRelPath(root, path);
      if (IsIgnoredPath(rel))
      {
        continue;
      }

      var txt = File.ReadAllText(path);
      if (externalAppRegex.Match(txt) is { Success: true } extMatch)
      {
        errors.Add($"{rel}: forbidden external application reference ({extMatch.Value})");
      }
    }

    var staleRegisterDocs = "RegisterDomain" + "Knowledge";
    foreach (var mdPath in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
      var rel = ToRelPath(root, mdPath);
      if (File.ReadAllText(mdPath).Contains(staleRegisterDocs, StringComparison.Ordinal))
      {
        errors.Add($"{rel}: stale {staleRegisterDocs} reference in docs (use PlatformModule.Docs)");
      }
    }

    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
  }

  private static Dictionary<string, string> LoadProjectRootNamespaces(string root)
  {
    var rootNsRegex = new Regex(@"<RootNamespace>([^<]+)</RootNamespace>");
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var csproj in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
    {
      if (IsIgnoredPath(ToRelPath(root, csproj)))
      {
        continue;
      }

      var dir = Path.GetDirectoryName(Path.GetFullPath(csproj))!;
      var txt = File.ReadAllText(csproj);
      if (rootNsRegex.Match(txt) is { Success: true } match)
      {
        map[dir] = match.Groups[1].Value.Trim();
        continue;
      }

      var projName = Path.GetFileNameWithoutExtension(csproj);
      map[dir] = projName.StartsWith("Air.", StringComparison.Ordinal)
        ? "Platform." + projName[4..]
        : projName.StartsWith("Demo", StringComparison.Ordinal) &&
          (projName.EndsWith(".Api", StringComparison.Ordinal) || projName.EndsWith(".Migrations", StringComparison.Ordinal)) &&
          projName != "Demo.Api"
          ? projName.Split('.')[0]
          : projName;
    }

    return map;
  }

  private static bool TryFindOwningProject(
    string csFullPath,
    Dictionary<string, string> csprojs,
    out string projDir,
    out string rootNs)
  {
    var full = Path.GetFullPath(csFullPath);
    var best = csprojs.Keys
      .Where(d => full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.Ordinal))
      .OrderByDescending(d => d.Length)
      .FirstOrDefault();

    if (best == null)
    {
      projDir = string.Empty;
      rootNs = string.Empty;
      return false;
    }

    projDir = best;
    rootNs = csprojs[best];
    return true;
  }

  private static string ComputeExpectedNamespace(string csFullPath, string projDir, string rootNs)
  {
    var fileDir = Path.GetDirectoryName(Path.GetFullPath(csFullPath))!;
    var relDir = Path.GetRelativePath(projDir, fileDir);
    return relDir == "."
      ? rootNs
      : $"{rootNs}.{relDir.Replace('\\', '.').Replace('/', '.')}";
  }

  private static int CountLines(string text)
  {
    var count = 1;
    foreach (var ch in text)
    {
      if (ch == '\n')
      {
        count++;
      }
    }

    return count;
  }

  private static bool IsIgnoredPath(string rel)
  {
    var segments = rel.Split('/');
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var seg = segments[i];
      if (seg.StartsWith('.') || seg is "bin" or "obj" or "node_modules" or "tmp")
      {
        return true;
      }
    }

    return false;
  }

  private static string ToRelPath(string root, string fullPath) =>
    Path.GetRelativePath(root, fullPath).Replace('\\', '/');

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AirPlatform.slnx")))
    {
      dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Could not locate AirPlatform.slnx root.");
  }
}
