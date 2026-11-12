using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Providers;

public sealed class AgyCliAgentProvider(AgentStore store, AgentOptions options) : IAgentProvider
{
  private readonly CliModelCache _modelCache = new();

  public string Handle => "Agy";
  public string Name => "Antigravity CLI (agy)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    var agyPath = ResolveExecutablePath();
    return _modelCache.GetOrLoadAsync(
      agyPath,
      forceRefresh,
      token => DiscoverModelsFromCliAsync(agyPath!, token),
      ct);
  }

  private async Task<List<AgentModelDto>> DiscoverModelsFromCliAsync(string agyPath, CancellationToken ct)
  {
    var psi = new ProcessStartInfo
    {
      FileName = agyPath,
      WorkingDirectory = store.WorkspaceDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = false,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8
    };
    CliTurnRunner.ScrubStartInfoEnvironment(psi);
    psi.ArgumentList.Add("models");

    using var proc = Process.Start(psi);
    if (proc is null)
    {
      return [];
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

    string stdout;
    try
    {
      stdout = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
      await proc.WaitForExitAsync(timeoutCts.Token);
    }
    catch (OperationCanceledException)
    {
      try
      {
        proc.Kill(entireProcessTree: true);
      }
      catch { }

      if (ct.IsCancellationRequested)
      {
        throw;
      }

      return [];
    }

    var parsed = new List<AgentModelDto>();
    foreach (var rawLine in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      var line = rawLine.Contains('\r') ? rawLine[(rawLine.LastIndexOf('\r') + 1)..] : rawLine;
      line = line.Trim();
      if (string.IsNullOrEmpty(line) || line.StartsWith("Fetching", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

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

    return parsed;
  }

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return CliTurnRunner.ResolveExecutablePath(
      "agy",
      Path.Combine(home, ".local", "bin", "agy"),
      "/usr/local/bin/agy",
      "/opt/homebrew/bin/agy");
  }

  internal static string FormatStdinTurnLine(string prompt) =>
    JsonSerializer.Serialize(
      new
      {
        @event = "user",
        message = new
        {
          role = "user",
          content = prompt
        }
      }) + "\n";

  internal static string ConfigureTurnStartInfo(
    ProcessStartInfo psi,
    string prompt,
    string? selectedModel,
    string? existingConvId,
    string sessionAttachmentsDir)
  {
    psi.ArgumentList.Add("--input-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--output-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--sandbox");
    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--print=");
    if (!string.IsNullOrWhiteSpace(selectedModel))
    {
      psi.ArgumentList.Add("--model");
      psi.ArgumentList.Add(selectedModel);
    }

    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(psi.WorkingDirectory);
    if (!string.IsNullOrWhiteSpace(sessionAttachmentsDir))
    {
      psi.ArgumentList.Add("--add-dir");
      psi.ArgumentList.Add(sessionAttachmentsDir);
    }

    if (!string.IsNullOrWhiteSpace(existingConvId))
    {
      psi.ArgumentList.Add("--conversation");
      psi.ArgumentList.Add(existingConvId);
    }

    return FormatStdinTurnLine(prompt);
  }

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? options.DefaultModel : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
      ?? options.DefaultModel;
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (_modelCache.CachedModels?.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);

    await foreach (var evt in CliTurnRunner.StreamTurnAsync(
      store,
      Handle,
      ResolveExecutablePath(),
      request,
      selectedModel,
      modelDisplayName,
      (psi, prompt, existingConvId, _, sessionAttachmentsDir, _) =>
        ConfigureTurnStartInfo(psi, prompt, selectedModel, existingConvId, sessionAttachmentsDir),
      (root, fullAssistantText, pendingEvents) =>
      {
        var evtType = root.TryGetProperty("event", out var eProp) ? eProp.GetString() : null;
        var emittedError = false;

        if (evtType == "init" && root.TryGetProperty("conversation_id", out var cidProp) && cidProp.GetString() is { Length: > 0 } cid)
        {
          store.SetConversationId(request.SessionId, request.Owner, cid, Handle);
        }
        else if (evtType == "error")
        {
          var errMsg = root.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String
            ? errProp.GetString()
            : (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String ? msgProp.GetString() : null);
          if (!string.IsNullOrWhiteSpace(errMsg))
          {
            emittedError = true;
            pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = errMsg });
          }
        }
        else if (evtType == "step_update" && root.TryGetProperty("step_update", out var step))
        {
          var stepType = step.TryGetProperty("step_type", out var stProp) ? stProp.GetString() : null;
          if (stepType == "agent_response" && step.TryGetProperty("text_delta", out var deltaProp))
          {
            var delta = deltaProp.GetString();
            if (!string.IsNullOrEmpty(delta))
            {
              fullAssistantText.Append(delta);
              pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = delta });
            }
          }
          else if (stepType is "tool_call" or "mcp_tool_call")
          {
            pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Status, Text = L["AgentStatusPreparing"] });
          }
        }

        return (evtType == "result", emittedError);
      },
      ct))
    {
      yield return evt;
    }
  }
}
