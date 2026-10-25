namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for Claude Code CLI (`claude -p --output-format stream-json`).
/// </summary>
public sealed class ClaudeCliAgentProvider(AgentStore store) : IAgentProvider
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly IReadOnlyList<AgentModelDto> Models =
  [
    new() { Id = "claude-opus-5-5-high", Name = "Claude Opus 5.5 (High)", Provider = "Claude" },
    new() { Id = "claude-sonnet-5-5-high", Name = "Claude Sonnet 5.5 (High)", Provider = "Claude" },
    new() { Id = "claude-opus-4-6", Name = "Claude Opus 4.6", Provider = "Claude" },
    new() { Id = "claude-sonnet-4-6", Name = "Claude Sonnet 4.6", Provider = "Claude" }
  ];

  public string Handle => "Claude";
  public string Name => "Claude CLI (claude)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult(Models);

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var candidates = new[]
    {
      "/opt/homebrew/bin/claude",
      "/usr/local/bin/claude",
      Path.Combine(home, ".local", "bin", "claude")
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
      var full = Path.Combine(dir, "claude");
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  private static (string Model, string? FallbackModel, string Effort) ResolveCliModelArgs(string selectedModel)
  {
    var lower = selectedModel.ToLowerInvariant();
    var effort = "high";
    var baseModel = lower;

    if (lower.EndsWith("-low", StringComparison.Ordinal))
    {
      effort = "low";
      baseModel = lower[..^4];
    }
    else if (lower.EndsWith("-medium", StringComparison.Ordinal))
    {
      effort = "medium";
      baseModel = lower[..^7];
    }
    else if (lower.EndsWith("-high", StringComparison.Ordinal))
    {
      effort = "high";
      baseModel = lower[..^5];
    }

    if (baseModel.StartsWith("claude-opus-5", StringComparison.Ordinal))
    {
      return (baseModel, "claude-opus-5", effort);
    }

    if (baseModel.StartsWith("claude-sonnet-5", StringComparison.Ordinal))
    {
      return (baseModel, "claude-sonnet-5", effort);
    }

    return (baseModel, null, effort);
  }

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var claudePath = ResolveExecutablePath();
    if (claudePath is null)
    {
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorCliMissing"]
      };
      yield break;
    }

    string? contextJson = null;
    try
    {
      var hasPhoto = request.Attachments.Count > 0;
      var includeReports = !hasPhoto;
      var includeRecentRecords = !hasPhoto || request.Message.Contains('#') || Regex.IsMatch(request.Message, @"\bid\s*\d+", RegexOptions.IgnoreCase);
      var ctxObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports, includeRecentRecords);
      contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
      if (includeReports && Encoding.UTF8.GetByteCount(contextJson) > 34_000)
      {
        var fullPath = Path.Combine(store.WorkspaceDirectory, $"context-{request.SessionId}.json");
        await File.WriteAllTextAsync(fullPath, contextJson, Encoding.UTF8, ct);
        var compactObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports: false, includeRecentRecords);
        contextJson = JsonSerializer.Serialize(compactObj, PromptJsonOptions);
      }
    }
    catch
    {
      // Ignore context prefetch errors
    }

    var prompt = AgentPromptBuilder.BuildPrompt(request, contextJson, store.WorkspaceDirectory);
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? "claude-opus-5-5-high" : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "claude-opus-5-5-high";
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (Models.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);
    var (cliModel, fallbackModel, effort) = ResolveCliModelArgs(selectedModel);

    var psi = new ProcessStartInfo
    {
      FileName = claudePath,
      WorkingDirectory = store.WorkspaceDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    psi.ArgumentList.Add("--print");
    psi.ArgumentList.Add(prompt);
    psi.ArgumentList.Add("--output-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--verbose");
    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--tools");
    psi.ArgumentList.Add("Read");
    psi.ArgumentList.Add("--model");
    psi.ArgumentList.Add(cliModel);
    if (!string.IsNullOrEmpty(fallbackModel) && !string.Equals(cliModel, fallbackModel, StringComparison.OrdinalIgnoreCase))
    {
      psi.ArgumentList.Add("--fallback-model");
      psi.ArgumentList.Add(fallbackModel);
    }

    psi.ArgumentList.Add("--effort");
    psi.ArgumentList.Add(effort);
    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(store.WorkspaceDirectory);

    using var process = new Process { StartInfo = psi };
    process.Start();
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
          if (type == "assistant" &&
              root.TryGetProperty("message", out var msgObj) &&
              msgObj.TryGetProperty("content", out var contentArr) &&
              contentArr.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in contentArr.EnumerateArray())
            {
              if (item.TryGetProperty("type", out var itProp) &&
                  itProp.GetString() == "text" &&
                  item.TryGetProperty("text", out var txtProp) &&
                  txtProp.GetString() is { Length: > 0 } chunk)
              {
                fullAssistantText.Append(chunk);
                yield return new AgentStreamEventDto
                {
                  Type = "delta",
                  Text = chunk
                };
              }
            }
          }
          else if (type == "result" &&
                   fullAssistantText.Length == 0 &&
                   root.TryGetProperty("result", out var resProp) &&
                   resProp.ValueKind == JsonValueKind.String &&
                   resProp.GetString() is { Length: > 0 } resText)
          {
            if (root.TryGetProperty("is_error", out var errFlag) && errFlag.ValueKind == JsonValueKind.True)
            {
              emittedError = true;
              yield return new AgentStreamEventDto
              {
                Type = "error",
                Text = resText
              };
            }
            else
            {
              fullAssistantText.Append(resText);
              yield return new AgentStreamEventDto
              {
                Type = "delta",
                Text = resText
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
        }
      }
    }
    finally
    {
      if (!process.HasExited)
      {
        try
        {
          process.Kill(entireProcessTree: true);
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

    if (!emittedError && fullAssistantText.Length == 0 && emittedProposals == 0 && !ct.IsCancellationRequested)
    {
      string? stderr = null;
      try
      {
        stderr = (await stderrTask).Trim();
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
