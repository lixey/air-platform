using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Providers;

public sealed class AgyCliAgentProvider(
  AgentStore store,
  AgentOptions options,
  IHostEnvironment? environment = null) : IAgentProvider
{
  private readonly CliModelCache _modelCache = new();

  public string Handle => "Agy";
  public string Name => "Antigravity CLI (agy)";
  public bool IsAvailable => CliTurnRunner.IsCliAllowed(options, environment) && ResolveExecutablePath() is not null;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    if (!CliTurnRunner.IsCliAllowed(options, environment))
    {
      return Task.FromResult<IReadOnlyList<AgentModelDto>>([]);
    }

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
    string sessionAttachmentsDir,
    string? mcpConfigPath = null,
    IReadOnlyList<string>? eagerMcpTools = null)
  {
    var hasPlatformMcp = AgyTurnConfigWriter.WriteMcpConfig(psi.WorkingDirectory, mcpConfigPath, eagerMcpTools);
    var settingsArg = AgyTurnConfigWriter.WriteTurnSettings(psi.WorkingDirectory, sessionAttachmentsDir, hasPlatformMcp);

    psi.ArgumentList.Add("--input-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--output-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--sandbox");
    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--settings");
    psi.ArgumentList.Add(settingsArg);
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

  internal static (bool ShouldBreak, bool EmittedError) HandleStreamEvent(
    JsonElement root,
    StringBuilder fullAssistantText,
    List<AgentStreamEventDto> pendingEvents,
    Action<string> onConversationId,
    string preparingStatusText)
  {
    var evtType = TryGetNonEmptyString(root, "event");
    var emittedError = false;

    if (evtType == "init" && TryGetNonEmptyString(root, "conversation_id") is { } cid)
    {
      onConversationId(cid);
    }
    else if (evtType == "error")
    {
      var errMsg = TryGetNonEmptyString(root, "error") ?? TryGetNonEmptyString(root, "message");
      if (!string.IsNullOrWhiteSpace(errMsg))
      {
        emittedError = true;
        pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = errMsg });
      }
    }
    else if (evtType == "step_update" && root.TryGetProperty("step_update", out var step))
    {
      var stepType = TryGetNonEmptyString(step, "step_type");
      if (stepType == "agent_response" && step.TryGetProperty("text_delta", out var deltaProp) && deltaProp.ValueKind == JsonValueKind.String)
      {
        var delta = deltaProp.GetString();
        if (!string.IsNullOrEmpty(delta))
        {
          fullAssistantText.Append(delta);
          pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = delta });
        }
      }
      else if (stepType is "tool" or "tool_call" or "mcp_tool_call")
      {
        var state = TryGetNonEmptyString(step, "state");
        if (state is null or "ACTIVE")
        {
          pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Status, Text = preparingStatusText });
        }
      }
    }
    else if (evtType == "result" && root.TryGetProperty("result", out var resultObj) && resultObj.ValueKind == JsonValueKind.Object)
    {
      if (TryGetNonEmptyString(resultObj, "error") is { } resErrText)
      {
        emittedError = true;
        pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = resErrText });
      }
      else if (fullAssistantText.Length == 0 && TryGetNonEmptyString(resultObj, "response") is { } fallbackText)
      {
        fullAssistantText.Append(fallbackText);
        pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = fallbackText });
      }
    }

    return (evtType == "result", emittedError);
  }

  private static string? TryGetNonEmptyString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var prop) &&
    prop.ValueKind == JsonValueKind.String &&
    prop.GetString() is { Length: > 0 } value
      ? value
      : null;

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
      (psi, prompt, existingConvId, mcpConfigPath, sessionAttachmentsDir, effectiveRequest) =>
      {
        var eagerMcpTools = effectiveRequest.Services?
          .GetServices<IMcpTool>()
          .Select(static t => t.Name)
          .Where(static n => !string.IsNullOrWhiteSpace(n))
          .Distinct(StringComparer.Ordinal)
          .ToList();
        return ConfigureTurnStartInfo(psi, prompt, selectedModel, existingConvId, sessionAttachmentsDir, mcpConfigPath, eagerMcpTools);
      },
      (root, fullAssistantText, pendingEvents) =>
        HandleStreamEvent(
          root,
          fullAssistantText,
          pendingEvents,
          cid => store.SetConversationId(request.SessionId, request.Owner, cid, Handle),
          L["AgentStatusPreparing"]),
      ct,
      environment))
    {
      yield return evt;
    }
  }
}
