namespace Platform.Agent.Providers.Internals;

internal static class AgyTurnConfigWriter
{
  private const int MaxEagerMcpTools = 16;

  private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

  private static readonly string[] DeniedPermissions =
  [
    "command(*)",
    "write_file(*)",
    "read_url(*)",
    "execute_url(*)",
    "download_url(*)"
  ];

  public static bool WriteMcpConfig(
    string? workingDirectory,
    string? mcpConfigPath,
    IReadOnlyList<string>? eagerMcpTools)
  {
    if (string.IsNullOrWhiteSpace(mcpConfigPath) ||
        string.IsNullOrWhiteSpace(workingDirectory) ||
        !File.Exists(mcpConfigPath))
    {
      return false;
    }

    using var doc = TryParseJson(mcpConfigPath);
    if (doc is null ||
        !doc.RootElement.TryGetProperty("mcpServers", out var servers) ||
        !servers.TryGetProperty("platform", out var platform) ||
        !platform.TryGetProperty("url", out var urlProp) ||
        urlProp.ValueKind != JsonValueKind.String ||
        urlProp.GetString() is not { Length: > 0 } serverUrl)
    {
      return false;
    }

    var headers = new Dictionary<string, string>(StringComparer.Ordinal);
    if (platform.TryGetProperty("headers", out var headersProp) && headersProp.ValueKind == JsonValueKind.Object)
    {
      foreach (var prop in headersProp.EnumerateObject())
      {
        if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { } value)
        {
          headers[prop.Name] = value;
        }
      }
    }

    Dictionary<string, object>? tools = null;
    if (eagerMcpTools is { Count: > 0 and <= MaxEagerMcpTools })
    {
      tools = new Dictionary<string, object>(StringComparer.Ordinal);
      foreach (var toolName in eagerMcpTools)
      {
        if (!string.IsNullOrWhiteSpace(toolName))
        {
          tools[toolName] = new { eager = true };
        }
      }
    }

    var agentsDir = Path.Combine(workingDirectory, ".agents");
    AgentStore.EnsurePrivateDirectory(agentsDir);
    var targetPath = Path.Combine(agentsDir, "mcp_config.json");

    var agyConfig = JsonSerializer.Serialize(
      new
      {
        mcpServers = new
        {
          platform = new
          {
            serverUrl,
            headers,
            tools = tools is { Count: > 0 } ? tools : null
          }
        }
      },
      CliEnvironmentHelper.PromptJsonOptions);
    File.WriteAllText(targetPath, agyConfig, Utf8NoBom);
    return true;
  }

  public static string WriteTurnSettings(
    string? workingDirectory,
    string? sessionAttachmentsDir,
    bool hasPlatformMcp)
  {
    var allow = new List<string>();
    var seenAllow = new HashSet<string>(StringComparer.Ordinal);

    if (hasPlatformMcp)
    {
      AddAllow(allow, seenAllow, "mcp(platform/*)");
    }

    AddReadFileAllowRules(workingDirectory, allow, seenAllow);
    AddReadFileAllowRules(sessionAttachmentsDir, allow, seenAllow);

    var settingsJson = JsonSerializer.Serialize(
      new
      {
        agentMode = string.Empty,
        toolPermission = "request-review",
        allowNonWorkspaceAccess = false,
        permissions = new
        {
          allow,
          deny = DeniedPermissions
        }
      },
      CliEnvironmentHelper.PromptJsonOptions);

    if (string.IsNullOrWhiteSpace(workingDirectory))
    {
      return settingsJson;
    }

    AgentStore.EnsurePrivateDirectory(workingDirectory);
    var settingsPath = Path.Combine(workingDirectory, "agy-settings.json");
    File.WriteAllText(settingsPath, settingsJson, Utf8NoBom);
    return settingsPath;
  }

  public static string ResolveCanonicalPath(string path)
  {
    var fullPath = Path.GetFullPath(path);
    var root = Path.GetPathRoot(fullPath);
    if (string.IsNullOrEmpty(root))
    {
      return fullPath;
    }

    var relative = fullPath[root.Length..];
    var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    var current = root;
    var stillResolving = true;

    foreach (var segment in segments)
    {
      current = Path.Combine(current, segment);
      if (!stillResolving)
      {
        continue;
      }

      if (!Directory.Exists(current) && !File.Exists(current))
      {
        stillResolving = false;
        continue;
      }

      try
      {
        if (Directory.ResolveLinkTarget(current, returnFinalTarget: true) is { } target)
        {
          current = target.FullName;
        }
      }
      catch
      {
        // Keep current path when link inspection is unavailable.
      }
    }

    return current;
  }

  private static JsonDocument? TryParseJson(string path)
  {
    try
    {
      return JsonDocument.Parse(File.ReadAllText(path));
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static void AddAllow(List<string> allow, HashSet<string> seenAllow, string rule)
  {
    if (seenAllow.Add(rule))
    {
      allow.Add(rule);
    }
  }

  private static void AddReadFileAllowRules(string? directoryPath, List<string> allow, HashSet<string> seenAllow)
  {
    if (string.IsNullOrWhiteSpace(directoryPath))
    {
      return;
    }

    var fullPath = Path.GetFullPath(directoryPath);
    AddAllow(allow, seenAllow, $"read_file({fullPath})");

    var canonicalPath = ResolveCanonicalPath(fullPath);
    if (!string.Equals(canonicalPath, fullPath, StringComparison.Ordinal))
    {
      AddAllow(allow, seenAllow, $"read_file({canonicalPath})");
    }
  }
}
