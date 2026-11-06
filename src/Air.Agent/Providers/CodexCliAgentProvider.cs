using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for OpenAI Codex CLI (`codex exec --json`).
/// </summary>
public sealed class CodexCliAgentProvider(AgentStore store) : IAgentProvider
{
  private static readonly Regex SlugMajorRegex = new(
    @"^[a-z]+-(?<major>\d+)(?:\.\d+)?(?:-|$)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private readonly CliModelCache _modelCache = new();

  public string Handle => "Codex";
  public string Name => "Codex CLI (codex)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    var cachePath = ResolveModelsCachePath();
    var cacheKey = cachePath is not null && File.Exists(cachePath)
      ? $"{cachePath}|{File.GetLastWriteTimeUtc(cachePath).Ticks}"
      : null;
    return _modelCache.GetOrLoadAsync(
      cacheKey,
      forceRefresh,
      token => DiscoverModelsFromCacheFileAsync(cachePath!, token),
      ct);
  }

  private static string? ResolveModelsCachePath()
  {
    var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
    if (string.IsNullOrWhiteSpace(codexHome))
    {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      codexHome = Path.Combine(home, ".codex");
    }

    var path = Path.Combine(codexHome, "models_cache.json");
    return File.Exists(path) ? path : null;
  }

  private static async Task<List<AgentModelDto>> DiscoverModelsFromCacheFileAsync(string cachePath, CancellationToken ct)
  {
    await using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 16384, useAsync: true);
    using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
    if (!doc.RootElement.TryGetProperty("models", out var modelsArr) || modelsArr.ValueKind != JsonValueKind.Array)
    {
      return [];
    }

    var entries = new List<(string Slug, string Title, int Major, HashSet<string> Efforts)>();
    var maxMajor = 0;

    foreach (var m in modelsArr.EnumerateArray())
    {
      var visibility = m.TryGetProperty("visibility", out var vProp) && vProp.ValueKind == JsonValueKind.String
        ? vProp.GetString()
        : null;
      if (!string.Equals(visibility, "list", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      var slug = m.TryGetProperty("slug", out var sProp) && sProp.ValueKind == JsonValueKind.String
        ? sProp.GetString()?.Trim()
        : null;
      if (string.IsNullOrWhiteSpace(slug))
      {
        continue;
      }

      var major = 0;
      var match = SlugMajorRegex.Match(slug);
      if (match.Success && int.TryParse(match.Groups["major"].Value, out var parsedMajor))
      {
        major = parsedMajor;
        if (major > maxMajor)
        {
          maxMajor = major;
        }
      }

      var rawTitle = m.TryGetProperty("display_name", out var dProp) && dProp.ValueKind == JsonValueKind.String
        ? dProp.GetString()?.Trim()
        : slug;
      var title = FormatModelDisplayTitle(rawTitle ?? slug);

      var efforts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (m.TryGetProperty("supported_reasoning_levels", out var rArr) && rArr.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in rArr.EnumerateArray())
        {
          if (item.TryGetProperty("effort", out var eProp) && eProp.ValueKind == JsonValueKind.String && eProp.GetString() is { Length: > 0 } eff)
          {
            efforts.Add(eff);
          }
        }
      }

      entries.Add((slug, title, major, efforts));
    }

    var result = new List<AgentModelDto>();
    foreach (var (slug, title, major, efforts) in entries)
    {
      if (maxMajor > 0 && major > 0 && major < maxMajor)
      {
        continue;
      }

      if (efforts.Contains("high") || efforts.Contains("medium") || efforts.Contains("low"))
      {
        if (efforts.Contains("high"))
        {
          result.Add(new AgentModelDto { Id = $"{slug}-high", Name = $"{title} (High)", Provider = "Codex" });
        }

        if (efforts.Contains("medium"))
        {
          result.Add(new AgentModelDto { Id = $"{slug}-medium", Name = $"{title} (Medium)", Provider = "Codex" });
        }

        if (efforts.Contains("low"))
        {
          result.Add(new AgentModelDto { Id = $"{slug}-low", Name = $"{title} (Low)", Provider = "Codex" });
        }
      }
      else
      {
        result.Add(new AgentModelDto { Id = slug, Name = title, Provider = "Codex" });
      }
    }

    return result;
  }

  private static string FormatModelDisplayTitle(string rawTitle)
  {
    if (rawTitle.Length <= 4)
    {
      return rawTitle;
    }

    var idx = rawTitle.IndexOf('-', 4);
    if (idx > 0 && idx + 1 < rawTitle.Length)
    {
      return $"{rawTitle[..idx]} {rawTitle[(idx + 1)..]}";
    }

    return rawTitle;
  }

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return CliTurnRunner.ResolveExecutablePath(
      "codex",
      "/opt/homebrew/bin/codex",
      "/usr/local/bin/codex",
      Path.Combine(home, ".local", "bin", "codex"));
  }

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? string.Empty : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (_modelCache.CachedModels?.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);
    var (cliModel, effort) = CliTurnRunner.ResolveCliModelArgs(selectedModel, "-xhigh", "-high", "-medium", "-low");

    await foreach (var evt in CliTurnRunner.StreamTurnAsync(
      store,
      Handle,
      ResolveExecutablePath(),
      request,
      selectedModel,
      modelDisplayName,
      (psi, prompt, existingConvId) =>
      {
        psi.ArgumentList.Add("exec");

        if (!string.IsNullOrWhiteSpace(existingConvId) && request.Attachments.Count == 0)
        {
          psi.ArgumentList.Add("resume");
          psi.ArgumentList.Add("--json");
          psi.ArgumentList.Add("--skip-git-repo-check");
          psi.ArgumentList.Add("--ignore-user-config");
          psi.ArgumentList.Add("--ignore-rules");
          psi.ArgumentList.Add("--add-dir");
          psi.ArgumentList.Add(psi.WorkingDirectory);
          if (!string.IsNullOrWhiteSpace(cliModel))
          {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(cliModel);
          }

          psi.ArgumentList.Add("-c");
          psi.ArgumentList.Add("sandbox_mode=\"read-only\"");
          psi.ArgumentList.Add("-c");
          psi.ArgumentList.Add($"model_reasoning_effort=\"{effort}\"");
          psi.ArgumentList.Add(existingConvId);
          psi.ArgumentList.Add("--");
          psi.ArgumentList.Add(prompt);
        }
        else
        {
          psi.ArgumentList.Add("--json");
          psi.ArgumentList.Add("--skip-git-repo-check");
          psi.ArgumentList.Add("--ignore-user-config");
          psi.ArgumentList.Add("--ignore-rules");
          psi.ArgumentList.Add("-s");
          psi.ArgumentList.Add("read-only");
          psi.ArgumentList.Add("--add-dir");
          psi.ArgumentList.Add(psi.WorkingDirectory);
          if (!string.IsNullOrWhiteSpace(cliModel))
          {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(cliModel);
          }

          psi.ArgumentList.Add("-c");
          psi.ArgumentList.Add($"model_reasoning_effort=\"{effort}\"");

          foreach (var att in request.Attachments)
          {
            var stagedCandidate = !string.IsNullOrWhiteSpace(att.LocalPath)
              ? Path.Combine(psi.WorkingDirectory, Path.GetFileName(att.LocalPath))
              : null;
            var pathToUse = !string.IsNullOrWhiteSpace(stagedCandidate) && File.Exists(stagedCandidate)
              ? stagedCandidate
              : att.LocalPath;
            if (!string.IsNullOrWhiteSpace(pathToUse) && File.Exists(pathToUse))
            {
              psi.ArgumentList.Add("-i");
              psi.ArgumentList.Add(pathToUse);
            }
          }

          psi.ArgumentList.Add("--");
          psi.ArgumentList.Add(prompt);
        }
      },
      (root, fullAssistantText, pendingEvents) =>
      {
        var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
        var emittedError = false;

        if (type == "thread.started" &&
            root.TryGetProperty("thread_id", out var tidProp) &&
            tidProp.ValueKind == JsonValueKind.String &&
            tidProp.GetString() is { Length: > 0 } tid)
        {
          store.SetConversationId(request.SessionId, request.Owner, tid, Handle);
        }
        else if (type == "item.started" && root.TryGetProperty("item", out var startedItem))
        {
          var itemType = startedItem.TryGetProperty("type", out var itProp) ? itProp.GetString() : null;
          if (itemType is "command_execution" or "mcp_tool_call" or "web_search")
          {
            pendingEvents.Add(new AgentStreamEventDto
            {
              Type = "status",
              Text = L["AgentStatusPreparing"]
            });
          }
        }
        else if (type == "item.completed" && root.TryGetProperty("item", out var completedItem))
        {
          var itemType = completedItem.TryGetProperty("type", out var itProp) ? itProp.GetString() : null;
          if (itemType == "agent_message" &&
              completedItem.TryGetProperty("text", out var txtProp) &&
              txtProp.ValueKind == JsonValueKind.String &&
              txtProp.GetString() is { Length: > 0 } chunk)
          {
            if (fullAssistantText.Length > 0 && !fullAssistantText.ToString().EndsWith('\n'))
            {
              fullAssistantText.Append("\n\n");
              chunk = "\n\n" + chunk;
            }

            fullAssistantText.Append(chunk);
            pendingEvents.Add(new AgentStreamEventDto
            {
              Type = "delta",
              Text = chunk
            });
          }
        }
        else if (type is "turn.failed" or "error" ||
                 root.TryGetProperty("is_error", out var isErrProp) && isErrProp.ValueKind == JsonValueKind.True)
        {
          string? errMsg = null;
          if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
          {
            errMsg = msgProp.GetString();
          }
          else if (root.TryGetProperty("error", out var errObj))
          {
            if (errObj.ValueKind == JsonValueKind.String)
            {
              errMsg = errObj.GetString();
            }
            else if (errObj.ValueKind == JsonValueKind.Object &&
                     errObj.TryGetProperty("message", out var nestedMsg) &&
                     nestedMsg.ValueKind == JsonValueKind.String)
            {
              errMsg = nestedMsg.GetString();
            }
          }

          if (!string.IsNullOrWhiteSpace(errMsg))
          {
            emittedError = true;
            pendingEvents.Add(new AgentStreamEventDto
            {
              Type = "error",
              Text = errMsg
            });
          }
        }

        return (type is "turn.completed" or "turn.failed" or "error", emittedError);
      },
      ct))
    {
      yield return evt;
    }
  }
}
