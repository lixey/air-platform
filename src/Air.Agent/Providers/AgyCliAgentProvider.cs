using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Air.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Air.Agent.Providers;

public sealed class AgyCliAgentProvider(AgentStore store) : IAgentProvider
{
  private string? _registeredMcpUrl;
  private readonly SemaphoreSlim _mcpInitLock = new(1, 1);

  public string Handle => "Agy";
  public string Name => "Antigravity CLI (agy)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

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

  private async Task EnsureMcpRegisteredAsync(string agyPath, string mcpUrl, CancellationToken ct)
  {
    if (string.Equals(_registeredMcpUrl, mcpUrl, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    await _mcpInitLock.WaitAsync(ct);
    try
    {
      if (string.Equals(_registeredMcpUrl, mcpUrl, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      var psi = new ProcessStartInfo
      {
        FileName = agyPath,
        WorkingDirectory = store.WorkspaceDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      psi.ArgumentList.Add("mcp");
      psi.ArgumentList.Add("add");
      psi.ArgumentList.Add("--type");
      psi.ArgumentList.Add("http");
      psi.ArgumentList.Add("air-mcp");
      psi.ArgumentList.Add(mcpUrl);

      using var proc = Process.Start(psi);
      if (proc is not null)
      {
        await proc.WaitForExitAsync(ct);
      }

      _registeredMcpUrl = mcpUrl;
    }
    catch
    {
      // Non-fatal: in-process tool execution fallback remains active.
    }
    finally
    {
      _mcpInitLock.Release();
    }
  }

  public async IAsyncEnumerable<AgentStreamEvent> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var agyPath = ResolveExecutablePath();
    if (agyPath is null)
    {
      yield return new AgentStreamEvent
      {
        Type = "error",
        Text = "CLI 'agy' не знайдено у системі."
      };
      yield break;
    }

    var mcpUrl = $"{request.ServerBaseUrl.TrimEnd('/')}/api/mcp";
    _ = EnsureMcpRegisteredAsync(agyPath, mcpUrl, CancellationToken.None);

    var tools = request.Services.GetServices<IMcpTool>().ToList();
    var existingConvId = store.GetConversationId(request.SessionId);

    // Pre-fetch compact context from any *_get_context tool so the agent has up-to-date IDs immediately
    string? contextJson = null;
    var contextTool = tools.FirstOrDefault(t => t.Name.EndsWith("_get_context", StringComparison.OrdinalIgnoreCase));
    if (contextTool is not null)
    {
      try
      {
        var ctxObj = await contextTool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), request.Services, ct);
        contextJson = JsonSerializer.Serialize(ctxObj, new JsonSerializerOptions(JsonSerializerDefaults.Web));
      }
      catch
      {
        // Ignore context prefetch errors
      }
    }

    var prompt = BuildPrompt(request, existingConvId is null, contextJson, tools);
    var selectedModel = string.IsNullOrWhiteSpace(request.Model)
      ? "gemini-3.8-flash-low"
      : request.Model.Trim();

    var psi = new ProcessStartInfo
    {
      FileName = agyPath,
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
    psi.ArgumentList.Add("--dangerously-skip-permissions");
    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--model");
    psi.ArgumentList.Add(selectedModel);
    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(store.WorkspaceDirectory);

    if (!string.IsNullOrWhiteSpace(existingConvId))
    {
      psi.ArgumentList.Add("--conversation");
      psi.ArgumentList.Add(existingConvId);
    }

    using var process = new Process { StartInfo = psi };
    process.Start();

    yield return new AgentStreamEvent
    {
      Type = "status",
      Text = request.Attachments.Count > 0 ? "Аналізую фото чека та дані…" : "Аналізую запит…"
    };

    var fullAssistantText = new StringBuilder();

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
            store.SetConversationId(request.SessionId, cid);
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
                yield return new AgentStreamEvent
                {
                  Type = "delta",
                  Text = delta
                };
              }
            }
            else if (stepType is "tool_call" or "mcp_tool_call")
            {
              yield return new AgentStreamEvent
              {
                Type = "status",
                Text = "Формую операцію…"
              };
            }
          }

          // Emit any proposals staged via MCP tool call during this step
          foreach (var staged in store.DrainPendingProposals(request.SessionId))
          {
            yield return new AgentStreamEvent
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
        try { process.Kill(entireProcessTree: true); } catch { }
      }
    }

    // Also check if the assistant emitted an inline <air_tool_call> block (fallback if it didn't invoke HTTP MCP directly)
    var completeText = fullAssistantText.ToString();
    var inlineProposals = await TryExecuteInlineToolCallsAsync(completeText, request, tools, ct);
    foreach (var p in inlineProposals)
    {
      yield return new AgentStreamEvent
      {
        Type = "proposal",
        Proposal = p
      };
    }

    foreach (var staged in store.DrainPendingProposals(request.SessionId))
    {
      yield return new AgentStreamEvent
      {
        Type = "proposal",
        Proposal = staged
      };
    }

    yield return new AgentStreamEvent
    {
      Type = "done",
      SessionId = request.SessionId
    };
  }

  private static async Task<List<AgentProposalDto>> TryExecuteInlineToolCallsAsync(
    string text,
    AgentTurnRequest request,
    IReadOnlyList<IMcpTool> tools,
    CancellationToken ct)
  {
    var results = new List<AgentProposalDto>();
    const string startTag = "<air_proposal>";
    const string endTag = "</air_proposal>";

    var idx = 0;
    while (idx < text.Length)
    {
      var start = text.IndexOf(startTag, idx, StringComparison.OrdinalIgnoreCase);
      if (start < 0) break;
      var end = text.IndexOf(endTag, start + startTag.Length, StringComparison.OrdinalIgnoreCase);
      if (end < 0) break;

      var json = text[(start + startTag.Length)..end].Trim();
      idx = end + endTag.Length;

      var proposeTool = tools.FirstOrDefault(t => t.Name == "fin_propose_transaction");
      if (proposeTool is not null && !string.IsNullOrWhiteSpace(json))
      {
        try
        {
          using var doc = JsonDocument.Parse(json);
          await proposeTool.ExecuteAsync(doc.RootElement.Clone(), request.Services, ct);
        }
        catch
        {
          // Ignore malformed JSON block
        }
      }
    }

    return results;
  }

  private static string BuildPrompt(
    AgentTurnRequest request,
    bool isFirstTurn,
    string? contextJson,
    IReadOnlyList<IMcpTool> tools)
  {
    var sb = new StringBuilder();

    sb.AppendLine("[SYSTEM INSTRUCTIONS FOR FINANCIAL ASSISTANT]");
    sb.AppendLine($"SessionId: {request.SessionId}");
    sb.AppendLine($"Preferred Language: {(request.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "English" : "Ukrainian (Українська)")}");
    sb.AppendLine("You are the built-in AI assistant of the MyPort Finance application.");
    sb.AppendLine("Your job is to help the user create financial transactions (Expenses, Incomes, Transfers) from text or receipt photos.");
    sb.AppendLine("IMPORTANT RULES:");
    sb.AppendLine("1. NEVER save transactions directly without user confirmation. Instead, prepare a transaction proposal.");
    sb.AppendLine("2. If the user attached receipt image file(s) listed below, you MUST first call `view_file` on each absolute file path to read the receipt store name, date, total amount, and line items (name, qty, price, amount).");
    sb.AppendLine("3. Match the store/merchant against `counterparties` in the Context JSON below:");
    sb.AppendLine("   - If an existing counterparty matches (e.g. 'Сільпо', 'АТБ', 'Rozetka', 'OKKO'), use its `id` (`counterpartyId`) and its `defaultCategoryId` (unless another category fits better).");
    sb.AppendLine("   - If the counterparty is NOT in `counterparties`, set `counterpartyId: null` and set `newCounterpartyName` to the clean merchant name from the receipt/text so a new counterparty will be created upon confirmation!");
    sb.AppendLine("4. If the user did not specify an account, pick the most appropriate account from `accounts` (e.g. the primary card/account in the matching currency) and mention it in your brief comment.");
    sb.AppendLine("5. To display the confirmation card with the 'Підтверджую' button in the UI, output a short friendly message (1-3 sentences) AND include an `<air_proposal>` JSON block at the end of your response with this exact schema:");
    sb.AppendLine("""
<air_proposal>
{
  "sessionId": "<SessionId>",
  "kind": "Expense",
  "date": "YYYY-MM-DD",
  "accountId": 1,
  "toAccountId": null,
  "amount": 125.50,
  "counterpartyId": 3,
  "newCounterpartyName": null,
  "categoryId": 5,
  "memberId": null,
  "tagIds": [],
  "note": "Optional comment",
  "attachmentIds": ["<attachmentId>"],
  "lines": [
    { "name": "Item name", "qty": 1, "price": 125.50, "amount": 125.50, "categoryId": 5 }
  ]
}
</air_proposal>
""");
    sb.AppendLine("Do NOT wrap `<air_proposal>` in markdown code fences (` ``` `) — output `<air_proposal>{...}</air_proposal>` directly so the UI renders it as an interactive confirmation card.");

    if (!string.IsNullOrWhiteSpace(contextJson))
    {
      sb.AppendLine();
      sb.AppendLine("[CURRENT DATABASE CONTEXT (Accounts, Categories, Counterparties, Members, Tags)]");
      sb.AppendLine(contextJson);
    }

    if (request.Attachments.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("[ATTACHED RECEIPT FILES — USE view_file TO INSPECT]");
      foreach (var att in request.Attachments)
      {
        sb.AppendLine($"- AttachmentId: \"{att.Id}\", FileName: \"{att.FileName}\", AbsolutePath: \"{att.LocalPath}\"");
      }
    }

    sb.AppendLine();
    sb.AppendLine("[USER MESSAGE]");
    sb.AppendLine(string.IsNullOrWhiteSpace(request.Message)
      ? "Розбери прикріплений чек та підготуй операцію."
      : request.Message);

    return sb.ToString();
  }
}
