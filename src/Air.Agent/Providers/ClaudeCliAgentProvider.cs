using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for Claude Code CLI (`claude -p --output-format stream-json`).
/// </summary>
public sealed class ClaudeCliAgentProvider(AgentStore store) : IAgentProvider
{
  private static readonly IReadOnlyList<AgentModelDto> CuratedClaudeModels =
  [
    new() { Id = "opus-high", Name = "Claude Opus (High)", Provider = "Claude" },
    new() { Id = "opus-medium", Name = "Claude Opus (Medium)", Provider = "Claude" },
    new() { Id = "opus-low", Name = "Claude Opus (Low)", Provider = "Claude" },
    new() { Id = "sonnet-high", Name = "Claude Sonnet (High)", Provider = "Claude" },
    new() { Id = "sonnet-medium", Name = "Claude Sonnet (Medium)", Provider = "Claude" },
    new() { Id = "sonnet-low", Name = "Claude Sonnet (Low)", Provider = "Claude" },
    new() { Id = "haiku", Name = "Claude Haiku", Provider = "Claude" }
  ];

  public string Handle => "Claude";
  public string Name => "Claude CLI (claude)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>(IsAvailable ? CuratedClaudeModels : []);

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return CliTurnRunner.ResolveExecutablePath(
      "claude",
      "/opt/homebrew/bin/claude",
      "/usr/local/bin/claude",
      Path.Combine(home, ".local", "bin", "claude"));
  }

  internal static string ConfigureTurnStartInfo(
    ProcessStartInfo psi,
    string prompt,
    string cliModel,
    string effort,
    string? existingConvId,
    string? mcpConfigPath,
    string sessionAttachmentsDir)
  {
    psi.ArgumentList.Add("--print");
    psi.ArgumentList.Add("--input-format");
    psi.ArgumentList.Add("text");
    psi.ArgumentList.Add("--output-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--verbose");
    psi.ArgumentList.Add("--safe-mode");
    psi.ArgumentList.Add("--strict-mcp-config");
    if (!string.IsNullOrWhiteSpace(mcpConfigPath))
    {
      psi.ArgumentList.Add("--mcp-config");
      psi.ArgumentList.Add(mcpConfigPath);
    }

    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--tools");
    psi.ArgumentList.Add("Read");
    psi.ArgumentList.Add("--allowedTools");
    psi.ArgumentList.Add(
      !string.IsNullOrWhiteSpace(mcpConfigPath)
        ? "Read,mcp__platform__platform_catalog_list,mcp__platform__platform_catalog_describe,mcp__platform__platform_records_query,mcp__platform__platform_proposal_stage"
        : "Read");
    if (!string.IsNullOrWhiteSpace(cliModel))
    {
      psi.ArgumentList.Add("--model");
      psi.ArgumentList.Add(cliModel);
    }

    psi.ArgumentList.Add("--effort");
    psi.ArgumentList.Add(effort);
    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(psi.WorkingDirectory);
    if (!string.IsNullOrWhiteSpace(sessionAttachmentsDir))
    {
      psi.ArgumentList.Add("--add-dir");
      psi.ArgumentList.Add(sessionAttachmentsDir);
    }

    if (!string.IsNullOrWhiteSpace(existingConvId))
    {
      psi.ArgumentList.Add("--resume");
      psi.ArgumentList.Add(existingConvId);
    }

    return prompt;
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
      : (CuratedClaudeModels.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);
    var (cliModel, effort) = CliTurnRunner.ResolveCliModelArgs(selectedModel, "-high", "-medium", "-low");

    await foreach (var evt in CliTurnRunner.StreamTurnAsync(
      store,
      Handle,
      ResolveExecutablePath(),
      request,
      selectedModel,
      modelDisplayName,
      (psi, prompt, existingConvId, mcpConfigPath, sessionAttachmentsDir, _) =>
        ConfigureTurnStartInfo(psi, prompt, cliModel, effort, existingConvId, mcpConfigPath, sessionAttachmentsDir),
      (root, fullAssistantText, pendingEvents) =>
      {
        if (root.TryGetProperty("session_id", out var sidProp) &&
            sidProp.ValueKind == JsonValueKind.String &&
            sidProp.GetString() is { Length: > 0 } sid)
        {
          store.SetConversationId(request.SessionId, request.Owner, sid, Handle);
        }

        var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
        var emittedError = false;

        if (type == "assistant" &&
            root.TryGetProperty("message", out var msgObj) &&
            msgObj.TryGetProperty("content", out var contentArr) &&
            contentArr.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in contentArr.EnumerateArray())
          {
            var itemType = item.TryGetProperty("type", out var itProp) ? itProp.GetString() : null;
            if (itemType == "text" &&
                item.TryGetProperty("text", out var txtProp) &&
                txtProp.GetString() is { Length: > 0 } chunk)
            {
              fullAssistantText.Append(chunk);
              pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = chunk });
            }
            else if (itemType == "tool_use")
            {
              pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Status, Text = L["AgentStatusPreparing"] });
            }
          }
        }
        else if (type == "result")
        {
          var resText = root.TryGetProperty("result", out var resProp) && resProp.ValueKind == JsonValueKind.String
            ? resProp.GetString()
            : null;
          if (root.TryGetProperty("is_error", out var errFlag) && errFlag.ValueKind == JsonValueKind.True)
          {
            emittedError = true;
            if (!string.IsNullOrWhiteSpace(resText))
            {
              pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = resText });
            }
          }
          else if (fullAssistantText.Length == 0 && !string.IsNullOrEmpty(resText))
          {
            fullAssistantText.Append(resText);
            pendingEvents.Add(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = resText });
          }
        }

        return (type == "result", emittedError);
      },
      ct))
    {
      yield return evt;
    }
  }
}
