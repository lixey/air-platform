namespace Platform.Agent.Providers;

public sealed class AgyCliAgentProvider(AgentStore store, AgentOptions options) : IAgentProvider
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private IReadOnlyList<AgentModelDto>? _cachedModels;
  private readonly SemaphoreSlim _modelsLock = new(1, 1);

  public string Handle => "Agy";
  public string Name => "Antigravity CLI (agy)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public async Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    if (!forceRefresh && _cachedModels is { Count: > 0 })
    {
      return _cachedModels;
    }

    var agyPath = ResolveExecutablePath();
    if (agyPath is null)
    {
      return [];
    }

    await _modelsLock.WaitAsync(ct);
    try
    {
      if (!forceRefresh && _cachedModels is { Count: > 0 })
      {
        return _cachedModels;
      }

      var psi = new ProcessStartInfo
      {
        FileName = agyPath,
        WorkingDirectory = store.WorkspaceDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8
      };
      psi.ArgumentList.Add("models");

      using var proc = Process.Start(psi);
      if (proc is null)
      {
        return [];
      }

      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

      var stdout = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
      await proc.WaitForExitAsync(timeoutCts.Token);

      var parsed = new List<AgentModelDto>();
      foreach (var rawLine in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        // Strip spinner frames separated by '\r'
        var line = rawLine.Contains('\r') ? rawLine[(rawLine.LastIndexOf('\r') + 1)..] : rawLine;
        line = line.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("Fetching", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        // agy models separates <id> and <name> with tabs (\t) or spaces
        var firstWs = line.IndexOfAny([' ', '\t']);
        if (firstWs <= 0)
        {
          continue;
        }

        var id = line[..firstWs].Trim();
        var name = line[(firstWs + 1)..].Trim();
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && !id.Contains(':'))
        {
          parsed.Add(new AgentModelDto { Id = id, Name = name, Provider = Handle });
        }
      }

      if (parsed.Count > 0)
      {
        _cachedModels = parsed;
        return _cachedModels;
      }
    }
    catch
    {
      // Ignore CLI errors and return empty list
    }
    finally
    {
      _modelsLock.Release();
    }

    return _cachedModels ?? [];
  }

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var candidates = new[]
    {
      Path.Combine(home, ".local", "bin", "agy"),
      "/usr/local/bin/agy",
      "/opt/homebrew/bin/agy"
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
      var full = Path.Combine(dir, "agy");
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var agyPath = ResolveExecutablePath();
    if (agyPath is null)
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
    try
    {
      var hasPhoto = request.Attachments.Count > 0;
      var includeReports = !hasPhoto;
      var includeRecentRecords = !hasPhoto || request.Message.Contains('#') || Regex.IsMatch(request.Message, @"\bid\s*\d+", RegexOptions.IgnoreCase);
      var ctxObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports, includeRecentRecords);
      contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
      if (includeReports && Encoding.UTF8.GetByteCount(contextJson) > 98_000)
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
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? options.DefaultModel : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
      ?? options.DefaultModel;
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (_cachedModels?.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);

    var psi = new ProcessStartInfo
    {
      FileName = agyPath,
      WorkingDirectory = store.WorkspaceDirectory,
      RedirectStandardInput = true,
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
    psi.ArgumentList.Add("--sandbox");
    psi.ArgumentList.Add("--disable-slash-commands");
    if (!string.IsNullOrWhiteSpace(selectedModel))
    {
      psi.ArgumentList.Add("--model");
      psi.ArgumentList.Add(selectedModel);
    }

    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(store.WorkspaceDirectory);

    if (!string.IsNullOrWhiteSpace(existingConvId) && request.Attachments.Count == 0)
    {
      psi.ArgumentList.Add("--conversation");
      psi.ArgumentList.Add(existingConvId);
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
          var evt = root.TryGetProperty("event", out var eProp) ? eProp.GetString() : null;

          if (evt == "init" && root.TryGetProperty("conversation_id", out var cidProp) && cidProp.GetString() is { Length: > 0 } cid)
          {
            store.SetConversationId(request.SessionId, request.Owner, cid, Handle);
          }
          else if (evt == "error")
          {
            var errMsg = root.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String
              ? errProp.GetString()
              : (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String ? msgProp.GetString() : null);
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
          else if (evt == "step_update" && root.TryGetProperty("step_update", out var step))
          {
            var stepType = step.TryGetProperty("step_type", out var stProp) ? stProp.GetString() : null;
            if (stepType == "agent_response" && step.TryGetProperty("text_delta", out var deltaProp))
            {
              var delta = deltaProp.GetString();
              if (!string.IsNullOrEmpty(delta))
              {
                fullAssistantText.Append(delta);
                yield return new AgentStreamEventDto
                {
                  Type = "delta",
                  Text = delta
                };
              }
            }
            else if (stepType is "tool_call" or "mcp_tool_call")
            {
              yield return new AgentStreamEventDto
              {
                Type = "status",
                Text = L["AgentStatusPreparing"]
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

          if (evt == "result")
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
      store.RemoveConversationId(request.SessionId, request.Owner, Handle);
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
