namespace Platform.Agent.Providers.Internals;

internal static class CliEnvironmentHelper
{
  internal static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly HashSet<string> AllowedExactEnvVars = new(StringComparer.OrdinalIgnoreCase)
  {
    "PATH", "HOME", "USER", "LOGNAME", "SHELL", "TMPDIR", "TEMP", "TMP",
    "LANG", "LC_ALL", "LC_CTYPE", "TERM", "SSL_CERT_FILE", "SSL_CERT_DIR",
    "NODE_EXTRA_CA_CERTS", "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
    "APPDATA", "LOCALAPPDATA", "USERPROFILE", "SYSTEMROOT", "COMSPEC", "PATHEXT"
  };

  private static readonly string[] AllowedEnvPrefixes =
  [
    "XDG_", "AGY_", "ANTHROPIC_", "CLAUDE_", "OPENAI_", "CODEX_", "GEMINI_"
  ];

  public static bool IsAllowedEnvironmentVariable(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    if (AllowedExactEnvVars.Contains(name))
    {
      return true;
    }

    foreach (var prefix in AllowedEnvPrefixes)
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  public static void ConfigureSanitizedEnvironment(
    IDictionary<string, string?> targetEnv,
    IReadOnlyDictionary<string, string?> sourceEnv,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null)
  {
    targetEnv.Clear();
    foreach (var (key, value) in sourceEnv)
    {
      if (value is not null && IsAllowedEnvironmentVariable(key))
      {
        targetEnv[key] = value;
      }
    }

    targetEnv["NO_COLOR"] = "1";
    if (!string.IsNullOrWhiteSpace(mcpTurnToken))
    {
      targetEnv["PLATFORM_MCP_TOKEN"] = mcpTurnToken;
    }

    if (!string.IsNullOrWhiteSpace(sessionId))
    {
      targetEnv["PLATFORM_SESSION_ID"] = sessionId;
    }

    if (!string.IsNullOrWhiteSpace(mcpServerUrl))
    {
      targetEnv["PLATFORM_MCP_URL"] = mcpServerUrl;
    }
  }

  public static void ScrubStartInfoEnvironment(
    ProcessStartInfo psi,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null)
  {
    var hostVars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
      if (entry.Key is string key)
      {
        hostVars[key] = entry.Value as string;
      }
    }

    ConfigureSanitizedEnvironment(psi.Environment, hostVars, mcpTurnToken, sessionId, mcpServerUrl);
  }

  public static string? ResolveExecutablePath(string binaryName, params string[] preferredPaths)
  {
    foreach (var candidate in preferredPaths)
    {
      if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
      {
        return candidate;
      }
    }

    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var full = Path.Combine(dir, binaryName);
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  public static (string Model, string Effort) ResolveCliModelArgs(string selectedModel, params string[] effortSuffixes)
  {
    var lower = selectedModel.Trim().ToLowerInvariant();
    var effort = "high";
    var baseModel = lower;

    foreach (var suffix in effortSuffixes)
    {
      if (lower.EndsWith(suffix, StringComparison.Ordinal) && lower.Length > suffix.Length)
      {
        effort = suffix[1..];
        baseModel = lower[..^suffix.Length];
        break;
      }
    }

    return (baseModel, effort);
  }

  public static async Task<string?> PrepareContextFilesAsync(
    AgentTurnRequest effectiveRequest,
    string turnWorkspaceDir,
    CancellationToken ct)
  {
    if (effectiveRequest.Services is null)
    {
      return null;
    }

    try
    {
      var contextFilePath = AgentStore.GetContextFilePath(turnWorkspaceDir);
      var recordsFilePath = AgentStore.GetRecordsFilePath(turnWorkspaceDir);
      var hasPhoto = effectiveRequest.Attachments.Any(a =>
        string.IsNullOrEmpty(a.ContentType) || a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
      var ctxObj = await AgentContextQueryService.BuildContextAsync(
        effectiveRequest.Services,
        effectiveRequest.Message,
        ct,
        includeReports: !hasPhoto,
        includeRecentRecords: true);

      var contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
      if (Encoding.UTF8.GetByteCount(contextJson) > 98_000)
      {
        await File.WriteAllTextAsync(contextFilePath, contextJson, Encoding.UTF8, ct);
        if (ctxObj is IDictionary<string, object?> ctxDict && ctxDict.Remove("reports"))
        {
          contextJson = JsonSerializer.Serialize(ctxDict, PromptJsonOptions);
        }
      }

      var extendedRecordsObj = await AgentContextQueryService.BuildExtendedWorkspaceRecordsAsync(effectiveRequest.Services, ct);
      await File.WriteAllTextAsync(recordsFilePath, JsonSerializer.Serialize(extendedRecordsObj, PromptJsonOptions), Encoding.UTF8, ct);
      return contextJson;
    }
    catch
    {
      return null;
    }
  }

  public static async Task<string?> WriteMcpConfigAsync(
    string turnWorkspaceDir,
    string? mcpServerUrl,
    string? mcpTurnToken,
    Encoding utf8NoBom,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(mcpServerUrl) || string.IsNullOrWhiteSpace(mcpTurnToken))
    {
      return null;
    }

    var mcpConfigPath = Path.Combine(turnWorkspaceDir, "mcp-config.json");
    var mcpConfigJson = JsonSerializer.Serialize(
      new
      {
        mcpServers = new
        {
          platform = new
          {
            type = "http",
            url = mcpServerUrl,
            headers = new Dictionary<string, string>
            {
              ["X-Platform-Mcp-Token"] = mcpTurnToken
            }
          }
        }
      },
      PromptJsonOptions);
    await File.WriteAllTextAsync(mcpConfigPath, mcpConfigJson, utf8NoBom, ct);
    return mcpConfigPath;
  }
}
