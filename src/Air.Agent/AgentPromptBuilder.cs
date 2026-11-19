using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentPromptBuilder
{
  private const int MaxInlinePromptBytes = 105_000;

  public static async Task<string> BuildPromptAsync(
    AgentTurnRequest request,
    string? contextJson,
    string? turnWorkspaceDir = null,
    CancellationToken ct = default)
  {
    var sb = new StringBuilder();
    var langDisplay = FormatLanguageDisplay();

    AppendSystemInstructions(sb, request.SessionId, langDisplay, request.CurrentPath);
    AppendAttachedFiles(sb, request.Attachments);
    AppendUnconfirmedSessionProposals(sb, request);
    await AppendSyncContextAsync(sb, request.SyncContext, turnWorkspaceDir, ct);
    AppendUserMessage(sb, request.Message);
    await AgentMemoryService.AppendDatabaseMemoryAndDomainKnowledgeAsync(sb, request, ct);

    if (!string.IsNullOrWhiteSpace(contextJson))
    {
      var currentBytes = Encoding.UTF8.GetByteCount(sb.ToString());
      var contextBytes = Encoding.UTF8.GetByteCount(contextJson);
      var fullContextFile = !string.IsNullOrWhiteSpace(turnWorkspaceDir)
        ? AgentStore.GetContextFilePath(turnWorkspaceDir)
        : null;

      if (currentBytes + contextBytes + 128 <= MaxInlinePromptBytes || string.IsNullOrWhiteSpace(turnWorkspaceDir))
      {
        sb.AppendLine();
        sb.AppendLine(fullContextFile != null && File.Exists(fullContextFile)
          ? $"[AVAILABLE FORMS, GRIDS, ACTIONS, AND DICTIONARIES CONTEXT JSON — FULL CONTEXT SAVED IN \"{fullContextFile}\"]"
          : "[AVAILABLE FORMS, GRIDS, ACTIONS, AND DICTIONARIES CONTEXT JSON]");
        sb.AppendLine(contextJson);
      }
      else
      {
        var contextPath = fullContextFile!;
        if (!File.Exists(contextPath))
        {
          await File.WriteAllTextAsync(contextPath, contextJson, Encoding.UTF8, ct);
        }

        sb.AppendLine();
        sb.AppendLine($"[AVAILABLE FORMS, GRIDS, ACTIONS, AND DICTIONARIES CONTEXT — FULL CONTEXT SAVED IN \"{contextPath}\" (USE read_file IF NEEDED)]");
        var budgetBytes = Math.Max(4096, MaxInlinePromptBytes - currentBytes - 256);
        sb.AppendLine(TruncateUtf8(contextJson, budgetBytes));
      }
    }

    return sb.ToString();
  }

  public static async Task<string> BuildDeltaPromptAsync(
    AgentTurnRequest request,
    string today,
    Dictionary<string, List<Dictionary<string, object?>>> mentionedRecords,
    string? changedMemoriesBlock = null,
    string? turnWorkspaceDir = null,
    CancellationToken ct = default)
  {
    var sb = new StringBuilder();
    sb.AppendLine("[TURN DELTA CONTEXT]");
    sb.AppendLine($"SessionId: {request.SessionId}");
    sb.AppendLine($"Today: {today}");
    sb.AppendLine($"Preferred Language: {FormatLanguageDisplay()}");
    if (SanitizeCurrentPath(request.CurrentPath) is { } safeDeltaPath)
    {
      sb.AppendLine($"CurrentScreen: {safeDeltaPath}");
    }

    AppendAttachedFiles(sb, request.Attachments);

    if (mentionedRecords.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("[MENTIONED RECORDS JSON]");
      sb.AppendLine(JsonSerializer.Serialize(mentionedRecords, WebJson));
    }

    if (!string.IsNullOrWhiteSpace(changedMemoriesBlock))
    {
      sb.AppendLine();
      sb.AppendLine(changedMemoriesBlock);
    }

    AppendUnconfirmedSessionProposals(sb, request);
    await AppendSyncContextAsync(sb, request.SyncContext, turnWorkspaceDir, ct);
    AppendUserMessage(sb, request.Message);

    return sb.ToString();
  }

  public static async Task<IReadOnlyList<string>> StageInlineProposalsAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    var (warnings, _) = await StageInlineProposalsAndMemoriesAsync(text, request, ct);
    return warnings;
  }

  public static async Task<(IReadOnlyList<string> Warnings, IReadOnlyList<AgentSavedMemoryDto> SavedMemories)> StageInlineProposalsAndMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    const string startTag = "<ui_proposal>";
    const string endTag = "</ui_proposal>";
    var warnings = new List<string>();
    var L = Localizer.Instance;
    var fallbackAttIds = request.Attachments.Select(a => a.Id).ToList();
    var jsonOpts = new JsonDocumentOptions
    {
      AllowTrailingCommas = true,
      CommentHandling = JsonCommentHandling.Skip
    };

    var elements = new List<JsonElement>();
    foreach (var json in AgentTagBlockExtractor.ExtractInnerBlocks(text, startTag, endTag))
    {
      try
      {
        using var doc = JsonDocument.Parse(json, jsonOpts);
        elements.Add(doc.RootElement.Clone());
      }
      catch (JsonException)
      {
        warnings.Add(L["AgentProposalStageWarning", L["RequestInvalid"]]);
      }
    }

    if (elements.Count > 0 && request.Services is not null)
    {
      elements = AgentTopologicalSorter.SortInlineProposalsTopologically(elements);

      var turnId = Guid.NewGuid().ToString("N")[..10];
      var store = request.Services.GetService<AgentStore>();
      var sessionId = AgentStore.NormalizeSessionId(request.SessionId);
      var stagedInTurn = new List<AgentProposalDto>();

      foreach (var el in elements)
      {
        try
        {
          var staged = await AgentProposalBuilder.StageProposalAsync(el, request.Services, ct, fallbackAttIds, turnId);
          stagedInTurn.Add(staged);
        }
        catch (FormRefusedException refusedEx)
        {
          var firstMsg = refusedEx.Messages.FirstOrDefault();
          var reason = firstMsg?.Detail ?? firstMsg?.Title ?? refusedEx.Title;
          warnings.Add(L["AgentProposalStageWarning", reason]);
        }
        catch (InvalidOperationException invEx)
        {
          warnings.Add(L["AgentProposalStageWarning", invEx.Message]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          warnings.Add(L["AgentProposalStageWarning", L["AgentErrorSave"]]);
        }
      }

      if (store != null && stagedInTurn.Count > 0)
      {
        var editedRecords = new List<(string Service, long RecordId)>();
        var createdNames = new List<(string Service, string Name)>();
        foreach (var p in stagedInTurn)
        {
          var normSvc = AgentFeatureRegistry.NormalizeServiceKey(p.Service);
          if (p.RecordId is > 0)
          {
            editedRecords.Add((normSvc, p.RecordId.Value));
          }
          else if (AgentStepGraphHelper.GetProposalShortLabel(p) is { Length: > 0 } shortLabel)
          {
            createdNames.Add((normSvc, shortLabel));
          }
        }

        store.SupersedeOverlappingTurnProposals(sessionId, request.Owner, turnId, editedRecords, createdNames);
        store.FinalizeTurnProposals(sessionId, request.Owner, turnId);
      }
    }

    var savedMemories = await AgentMemoryService.SaveInlineMemoriesAsync(text, request, ct);
    return (warnings, savedMemories);
  }

  public static Task<IReadOnlyList<AgentSavedMemoryDto>> SaveInlineMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct) =>
    AgentMemoryService.SaveInlineMemoriesAsync(text, request, ct);

  private static string FormatLanguageDisplay()
  {
    var uiCulture = CultureInfo.CurrentUICulture;
    return string.Equals(uiCulture.EnglishName, uiCulture.NativeName, StringComparison.OrdinalIgnoreCase)
      ? uiCulture.EnglishName
      : $"{uiCulture.EnglishName} ({uiCulture.NativeName})";
  }

  private static void AppendAttachedFiles(StringBuilder sb, IReadOnlyList<AgentAttachmentDto> attachments)
  {
    if (attachments.Count == 0)
    {
      return;
    }

    sb.AppendLine();
    sb.AppendLine("[ATTACHED FILES — USE read_file TO INSPECT]");
    foreach (var att in attachments)
    {
      sb.AppendLine($"- AttachmentId: \"{att.Id}\", FileName: \"{att.FileName}\", AbsolutePath: \"{att.LocalPath}\"");
    }
  }

  private static async Task AppendSyncContextAsync(
    StringBuilder sb,
    string? syncContext,
    string? turnWorkspaceDir,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(syncContext))
    {
      return;
    }

    var syncBytes = Encoding.UTF8.GetByteCount(syncContext);
    var currentSyncPrefixBytes = Encoding.UTF8.GetByteCount(sb.ToString());
    if (currentSyncPrefixBytes + syncBytes + 8192 <= MaxInlinePromptBytes || string.IsNullOrWhiteSpace(turnWorkspaceDir))
    {
      sb.AppendLine();
      sb.AppendLine("[SYNC CONTEXT JSON]");
      sb.AppendLine(syncContext);
    }
    else
    {
      var syncPath = AgentStore.GetSyncContextFilePath(turnWorkspaceDir);
      await File.WriteAllTextAsync(syncPath, syncContext, Encoding.UTF8, ct);
      sb.AppendLine();
      sb.AppendLine($"[SYNC CONTEXT JSON — FULL CONTEXT SAVED IN \"{syncPath}\" (USE read_file IF NEEDED)]");
      var syncBudgetBytes = Math.Min(24_000, Math.Max(4096, MaxInlinePromptBytes - currentSyncPrefixBytes - 8192));
      sb.AppendLine(TruncateUtf8(syncContext, syncBudgetBytes));
    }
  }

  private static void AppendUserMessage(StringBuilder sb, string? message)
  {
    sb.AppendLine();
    sb.AppendLine("[USER MESSAGE]");
    sb.AppendLine(
      string.IsNullOrWhiteSpace(message)
        ? Localizer.Instance["AgentDefaultPhotoPrompt"]
        : message);
  }

  private static void AppendSystemInstructions(StringBuilder sb, string sessionId, string langDisplay, string? currentPath)
  {
    sb.AppendLine("[SYSTEM INSTRUCTIONS FOR PLATFORM AI ASSISTANT]");
    sb.AppendLine($"SessionId: {sessionId}");
    sb.AppendLine($"Preferred Language: {langDisplay}");
    if (SanitizeCurrentPath(currentPath) is { } safePath)
    {
      sb.AppendLine($"CurrentScreen: {safePath}");
    }

    sb.AppendLine("You are the built-in AI assistant of the platform application.");
    sb.AppendLine("Your job is to help the user query analytics/records on demand and stage confirmation proposals using the available `forms`, `grids`, `actionServices`, and `dictionaries` in the Context JSON below.");
    sb.AppendLine("IMPORTANT RULES:");
    sb.AppendLine("1. NEVER save records directly without user confirmation. Stage proposal card(s) by outputting `<ui_proposal>` JSON block(s) (or calling `platform_proposal_stage`).");
    sb.AppendLine("2. CRITICAL TOOL RESTRICTION: NEVER call `run_command`, `bash`, `grep_search`, `find_by_name`, `list_dir`, or browser tools! You may ONLY call `read_file` / `Read` on exact paths listed below and the `platform_*` MCP tools (`platform_catalog_list`, `platform_catalog_describe`, `platform_records_query`, `platform_proposal_stage`).");
    sb.AppendLine("3. ON-DEMAND QUERYING: Only small dictionaries (<=50 rows) and `#ID`-mentioned records are preloaded. Use `platform_records_query` on any `grids` / `largeDictionaries` service to search or inspect records before proposing edits.");
    sb.AppendLine("4. CREATING, EDITING, MOVING STATUS & RUNNING BUSINESS ACTIONS:");
    sb.AppendLine("   - CREATE: `\"operation\": \"create\"`, omit `id`, populate `values`.");
    sb.AppendLine("   - EDIT: `\"operation\": \"update\"`, include `\"id\": <recordId>`, pass ONLY modified fields in `values`.");
    sb.AppendLine("   - CHILD GRIDS (`children`): pass `\"children\": [{ \"childKey\": \"<child.key>\", \"mode\": \"replace\", \"rows\": [{ \"<field.path>\": \"<value>\" }] }]` (`mode`: `\"replace\"` or `\"append\"`).");
    sb.AppendLine("   - STATUS TRANSITIONS (`transitions`): `\"operation\": \"move-status\"`, `\"service\": \"<service>\"`, `\"id\": <recordId>`, `\"transition\": \"<transition.handle>\"`, optional `\"ask\": { ... }`.");
    sb.AppendLine("   - BUSINESS ACTIONS (`actions` on forms or `actionServices`): `\"operation\": \"run-action\"`, `\"service\": \"<action.service>\"`, `\"action\": \"<action.handle>\"`, `\"id\": <recordId>` (or `\"selectedIds\": [...]` for `multi`, omit for `none`), optional `\"ask\": { ... }`.");
    sb.AppendLine("5. REFERENCE FIELDS & `$ref`: Use numeric `id` from `dictionaries`, exact name/code for `largeDictionaries`, or stage a prerequisite `<ui_proposal>` with `\"key\": \"<stepKey>\"` and link via `\"$ref:<stepKey>\"` (in `values`, `children[].rows`, `id`, `selectedIds`, or `ask`).");
    sb.AppendLine("6. `<ui_proposal>` SCHEMA (output directly without markdown code fences):");
    sb.AppendLine(
      """
      <ui_proposal>
      {
        "sessionId": "<SessionId>",
        "key": "<optional stepKey>",
        "operation": "create",
        "service": "<service>",
        "id": 123,
        "selectedIds": [123],
        "transition": "<optional transition.handle>",
        "action": "<optional action.handle>",
        "values": { "<field.path>": "<value or \"$ref:<stepKey>\">" },
        "ask": { "<askField.path>": "<value>" },
        "children": [
          { "childKey": "<child.key>", "mode": "replace", "rows": [{ "<childField.path>": "<value>" }] }
        ],
        "dependsOn": ["<optional stepKey>"],
        "attachmentIds": ["<attachmentId>"]
      }
      </ui_proposal>
      """);
    sb.AppendLine("7. RICH VISUAL BLOCKS (emit directly without markdown code fences when appropriate):");
    sb.AppendLine("   - `<ui_kpi>[{\"label\": \"...\", \"value\": 125000, \"currency\": \"<ISO>\", \"color\": \"emerald\", \"subtitle\": \"...\"}]</ui_kpi>` (optional `currency`, `isPercent: true`, or `isCount: true`).");
    sb.AppendLine("   - `<ui_chart>{\"key\": \"c1\", \"title\": \"...\", \"kind\": \"donut\", \"currency\": \"<ISO>\", \"slices\": [{\"name\": \"...\", \"value\": 1000, \"color\": \"blue\"}]}</ui_chart>`");
    sb.AppendLine("   - `<ui_chart>{\"key\": \"c2\", \"title\": \"...\", \"kind\": \"bar\", \"currency\": \"<ISO>\", \"categories\": [\"Jan\", \"Feb\"], \"series\": [{\"name\": \"Sales\", \"color\": \"blue\", \"data\": [100, 200]}]}</ui_chart>` (`kind`: `bar`, `line`, `area`, `stacked-bar`, `horizontal-bar`, `donut`, `pie`).");
    sb.AppendLine("   - `<ui_table>{\"title\": \"...\", \"columns\": [{\"key\": \"name\", \"label\": \"...\", \"kind\": \"text\"}], \"rows\": [{\"name\": \"...\"}]}</ui_table>` (`kind`: `text`, `number`, `money`, `percent`, `badge`, `date`, `datetime`; optional `bottomRows`) or `<ui_html>...</ui_html>`.");
    sb.AppendLine("   - Allowed `color`: `slate`, `red`, `orange`, `amber`, `emerald`, `teal`, `cyan`, `sky`, `blue`, `indigo`, `violet`, `purple`, `fuchsia`, `pink`, `rose`.");
    sb.AppendLine("8. MEMORY & RULES (`<agent_memory>` vs `<ui_proposal>`): For personal user preferences or facts ONLY, emit `<agent_memory>{\"kind\": \"Preference\", \"title\": \"Short title\", \"content\": \"Clear preference or fact\"}</agent_memory>` (`kind`: `\"Preference\"` or `\"Fact\"`). Never emit `kind: \"Rule\"` in `<agent_memory>`; stage a `<ui_proposal>` for `AgentMemory` instead.");
  }

  private static void AppendUnconfirmedSessionProposals(StringBuilder sb, AgentTurnRequest request)
  {
    var store = request.Services?.GetService<AgentStore>();
    var pending = store?.GetSessionProposals(request.SessionId, request.Owner).Where(p => p.Status == "pending").ToList();
    if (pending is not { Count: > 0 })
    {
      return;
    }

    var summaryItems = new List<object>(pending.Count);
    foreach (var prop in pending)
    {
      Dictionary<string, object?>? values = null;
      try
      {
        var payload = prop.Payload.Deserialize<StagedProposalPayload>(WebJson);
        if (payload != null)
        {
          values = payload.ExplicitFields.Count > 0
            ? payload.ExplicitFields
              .Where(k => payload.Values.ContainsKey(k))
              .ToDictionary(k => k, k => payload.Values[k], StringComparer.OrdinalIgnoreCase)
            : payload.Values;

          foreach (var nr in payload.NewReferences.Where(r => r.LineIndex == null && !string.IsNullOrEmpty(r.StepKey)))
          {
            values ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            values[nr.FieldPath] = $"$ref:{nr.StepKey}";
          }
        }
      }
      catch { }

      summaryItems.Add(new
      {
        step = prop.StepNumber,
        key = prop.StepKey,
        operation = prop.Operation,
        service = prop.Service,
        transition = prop.Transition,
        action = prop.Action,
        id = prop.RecordId,
        title = prop.Title,
        values
      });
    }

    sb.AppendLine();
    sb.AppendLine("[CURRENT UNCONFIRMED PROPOSALS IN THIS SESSION — IF USER ASKS TO REVISE/ADJUST THEM, EMIT THE COMPLETE UPDATED BUNDLE]");
    sb.AppendLine(JsonSerializer.Serialize(summaryItems, WebJson));
  }

  public static string? SanitizeCurrentPath(string? rawPath)
  {
    if (string.IsNullOrWhiteSpace(rawPath))
    {
      return null;
    }

    var trimmed = rawPath.Trim();
    var cutIdx = trimmed.IndexOfAny(['?', '#', '\r', '\n']);
    var pathOnly = cutIdx >= 0 ? trimmed[..cutIdx].Trim() : trimmed;
    if (pathOnly.Length is 0 or > 128 || pathOnly[0] != '/')
    {
      return null;
    }

    foreach (var ch in pathOnly)
    {
      var allowed = char.IsAsciiLetterOrDigit(ch) || ch is '/' or '_' or '-' or '.' or '~';
      if (!allowed)
      {
        return null;
      }
    }

    return pathOnly;
  }

  private static string TruncateUtf8(string text, int maxBytes)
  {
    if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
    {
      return text;
    }

    var bytes = Encoding.UTF8.GetBytes(text);
    var len = maxBytes;
    while (len > 0 && (bytes[len] & 0b1100_0000) == 0b1000_0000)
    {
      len--;
    }

    return Encoding.UTF8.GetString(bytes, 0, len);
  }
}
