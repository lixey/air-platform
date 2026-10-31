namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for OpenAI Codex CLI (`codex exec --json`).
/// </summary>
public sealed class CodexCliAgentProvider(AgentStore store) : IAgentProvider
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly Regex SlugMajorRegex = new(
    @"^[a-z]+-(?<major>\d+)(?:\.\d+)?(?:-|$)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private IReadOnlyList<AgentModelDto>? _cachedModels;
  private DateTime _cachedCacheWriteUtc;
  private string? _cachedCachePath;
  private readonly SemaphoreSlim _modelsLock = new(1, 1);

  public string Handle => "Codex";
  public string Name => "Codex CLI (codex)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public async Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    var cachePath = ResolveModelsCachePath();
    if (cachePath is null || !File.Exists(cachePath))
    {
      return [];
    }

    var writeUtc = File.GetLastWriteTimeUtc(cachePath);
    if (!forceRefresh &&
        _cachedModels is { Count: > 0 } &&
        string.Equals(_cachedCachePath, cachePath, StringComparison.Ordinal) &&
        _cachedCacheWriteUtc == writeUtc)
    {
      return _cachedModels;
    }

    await _modelsLock.WaitAsync(ct);
    try
    {
      if (!forceRefresh &&
          _cachedModels is { Count: > 0 } &&
          string.Equals(_cachedCachePath, cachePath, StringComparison.Ordinal) &&
          _cachedCacheWriteUtc == writeUtc)
      {
        return _cachedModels;
      }

      var discovered = await DiscoverModelsFromCacheFileAsync(cachePath, ct);
      if (discovered.Count > 0)
      {
        _cachedModels = discovered;
        _cachedCachePath = cachePath;
        _cachedCacheWriteUtc = writeUtc;
        return _cachedModels;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Ignore cache file read errors
    }
    finally
    {
      _modelsLock.Release();
    }

    return _cachedModels ?? [];
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
    var candidates = new[]
    {
      "/opt/homebrew/bin/codex",
      "/usr/local/bin/codex",
      Path.Combine(home, ".local", "bin", "codex")
    };

    foreach (var candidate in candidates)
    {
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var full = Path.Combine(dir, "codex");
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  private static (string Model, string Effort) ResolveCliModelArgs(string selectedModel)
  {
    var lower = selectedModel.Trim().ToLowerInvariant();
    var effort = "high";
    var baseModel = lower;

    string[] effortSuffixes = ["-xhigh", "-high", "-medium", "-low"];
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

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var codexPath = ResolveExecutablePath();
    if (codexPath is null)
    {
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorCliMissing"]
      };
      yield break;
    }

    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, Handle);

    string? contextJson = null;
    string? tempContextFile = null;
    try
    {
      var hasPhoto = request.Attachments.Count > 0;
      var includeReports = !hasPhoto;
      var includeRecentRecords = !hasPhoto || request.Message.Contains('#') || Regex.IsMatch(request.Message, @"\bid\s*\d+", RegexOptions.IgnoreCase);
      var ctxObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports, includeRecentRecords);
      contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
      if (includeReports && Encoding.UTF8.GetByteCount(contextJson) > 98_000)
      {
        tempContextFile = Path.Combine(store.WorkspaceDirectory, $"context-{AgentStore.NormalizeSessionId(request.SessionId)}.json");
        await File.WriteAllTextAsync(tempContextFile, contextJson, Encoding.UTF8, ct);
        var compactObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports: false, includeRecentRecords);
        contextJson = JsonSerializer.Serialize(compactObj, PromptJsonOptions);
      }
    }
    catch
    {
      // Ignore context prefetch errors
    }

    var prompt = AgentPromptBuilder.BuildPrompt(request, contextJson, store.WorkspaceDirectory);
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? string.Empty : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (_cachedModels?.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);
    var (cliModel, effort) = ResolveCliModelArgs(selectedModel);

    var psi = new ProcessStartInfo
    {
      FileName = codexPath,
      WorkingDirectory = store.WorkspaceDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    psi.ArgumentList.Add("exec");

    if (!string.IsNullOrWhiteSpace(existingConvId) && request.Attachments.Count == 0)
    {
      psi.ArgumentList.Add("resume");
      psi.ArgumentList.Add("--json");
      psi.ArgumentList.Add("--skip-git-repo-check");
      psi.ArgumentList.Add("--ignore-user-config");
      psi.ArgumentList.Add("--ignore-rules");
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
      psi.ArgumentList.Add(store.WorkspaceDirectory);
      if (!string.IsNullOrWhiteSpace(cliModel))
      {
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(cliModel);
      }

      psi.ArgumentList.Add("-c");
      psi.ArgumentList.Add($"model_reasoning_effort=\"{effort}\"");

      foreach (var att in request.Attachments)
      {
        if (!string.IsNullOrWhiteSpace(att.LocalPath) && File.Exists(att.LocalPath))
        {
          psi.ArgumentList.Add("-i");
          psi.ArgumentList.Add(att.LocalPath);
        }
      }

      psi.ArgumentList.Add("--");
      psi.ArgumentList.Add(prompt);
    }

    using var process = new Process { StartInfo = psi };
    process.Start();
    process.StandardInput.Close();
    var stderrTask = process.StandardError.ReadToEndAsync(ct);

    yield return new AgentStreamEventDto
    {
      Type = "status",
      Text = request.Attachments.Count > 0 ? L["AgentStatusReadingPhoto"] : L["AgentStatusAnalyzing"],
      ModelId = selectedModel,
      ModelName = modelDisplayName
    };

    var fullAssistantText = new StringBuilder();
    var emittedProposals = 0;
    var emittedError = false;

    try
    {
      while (!ct.IsCancellationRequested)
      {
        var line = await process.StandardOutput.ReadLineAsync(ct);
        if (line is null)
        {
          break;
        }

        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{'))
        {
          continue;
        }

        JsonDocument? doc = null;
        try
        {
          doc = JsonDocument.Parse(line);
        }
        catch
        {
          continue;
        }

        using (doc)
        {
          var root = doc.RootElement;
          var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;

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
              yield return new AgentStreamEventDto
              {
                Type = "status",
                Text = L["AgentStatusPreparing"]
              };
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
              yield return new AgentStreamEventDto
              {
                Type = "delta",
                Text = chunk
              };
            }
          }
          else if (type == "turn.failed")
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
              yield return new AgentStreamEventDto
              {
                Type = "error",
                Text = errMsg
              };
            }
          }

          foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
          {
            emittedProposals++;
            yield return new AgentStreamEventDto
            {
              Type = "proposal",
              Proposal = staged
            };
          }

          if (type is "turn.completed" or "turn.failed")
          {
            break;
          }
        }
      }
    }
    finally
    {
      if (!process.HasExited && !ct.IsCancellationRequested)
      {
        try
        {
          await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch { }
      }

      if (!process.HasExited)
      {
        try
        {
          process.Kill(entireProcessTree: true);
        }
        catch { }
      }

      if (tempContextFile is not null)
      {
        try
        {
          File.Delete(tempContextFile);
        }
        catch { }
      }
    }

    var completeText = fullAssistantText.ToString();
    await AgentPromptBuilder.StageInlineProposalsAsync(completeText, request, ct);

    foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
    {
      emittedProposals++;
      yield return new AgentStreamEventDto
      {
        Type = "proposal",
        Proposal = staged
      };
    }

    if (emittedError || fullAssistantText.Length == 0 && emittedProposals == 0)
    {
      store.RemoveConversationId(request.SessionId, request.Owner, Handle);
    }

    if (!emittedError && fullAssistantText.Length == 0 && emittedProposals == 0 && !ct.IsCancellationRequested)
    {
      string? stderr = null;
      try
      {
        stderr = (await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), ct)).Trim();
      }
      catch { }

      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = !string.IsNullOrWhiteSpace(stderr) ? stderr : L["AgentErrorRequest"]
      };
    }

    yield return new AgentStreamEventDto
    {
      Type = "done",
      SessionId = request.SessionId,
      ModelId = selectedModel,
      ModelName = modelDisplayName
    };
  }
}
